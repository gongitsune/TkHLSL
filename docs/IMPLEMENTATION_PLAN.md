# TkHLSL 実装計画

このドキュメントは TkHLSL の設計メモ兼ロードマップです。開発者本人が読み返し、フェーズごとの実装を進める際の指針として使う「生きたドキュメント」であり、フェーズが進むごとに更新します。

## 1. 概要

TkHLSL は、Unity の ComputeShader（HLSL）ソースを解析し、カーネルごとに使用しているリソース（バッファ・テクスチャ・サンプラー・cbuffer など）を構造化データとして提供する**HLSL 解析専用ライブラリ**です。

TkHLSL 自身は **Roslyn Analyzer / Source Generator を実装しません**。それらは別の外部ライブラリが担当し、そのライブラリが TkHLSL を参照して解析結果（`HlslCompilationResult` など）を取得し、C# バインディングコードを生成します。TkHLSL は `Microsoft.CodeAnalysis.*` を一切参照しない、プレーンな .NET クラスライブラリとして設計します。

TkHLSL のアーキテクチャは、Rust 製のシェーダー変換基盤 **[naga](https://github.com/gfx-rs/wgpu/tree/trunk/naga)**（gfx-rs/wgpu プロジェクト。WGSL/GLSL/SPIR-V/HLSL/MSL 間のシェーダー変換を担う、実運用で枯れたアーキテクチャ）を参考にします。naga は「フロントエンド（各言語パーサー）→ 中間表現（IR）→ 検証・解析（validator/analyzer）→ バックエンド（各言語コード生成）」という明確なパイプラインを持ち、特に **Arena/Handle パターン**によって IR 全体を所有権・借用の問題なく効率的に扱える設計になっています。TkHLSL はこの「フロントエンド + IR + 解析」の部分（naga でいう `front` + `valid` 相当）だけを実装し、「バックエンド」（naga でいう `back` 相当、コード生成）は担いません。それは外部の Roslyn Analyzer ライブラリの責務です。

## 2. 参考アーキテクチャ: naga

### 2.1 Arena / Handle パターン

naga は IR のほとんどの要素を、生ポインタや参照ではなく「配列＋インデックス」で表現します（[`naga/src/arena/mod.rs`](https://github.com/gfx-rs/wgpu/blob/trunk/naga/src/arena/mod.rs)）。

> To improve translator performance and reduce memory usage, most structures are stored in an `Arena`. An `Arena<T>` stores a series of `T` values, indexed by `Handle<T>` values, which are just wrappers around integer indexes. For example, a `Function`'s expressions are stored in an `Arena<Expression>`, and compound expressions refer to their sub-expressions via `Handle<Expression>` values.

- **`Arena<T>`**: `T` の値を順に格納する配列。追加すると `Handle<T>`（型付きの整数インデックス）が返る。要素同士の参照は全てこの `Handle<T>` 経由で行う。
- **`UniqueArena<T>`**: `Arena` と同様だが、`Eq`/`Hash` に基づき同じ値は1つしか保持しない（挿入すると既存の値と同じなら同じ `Handle` が返る）。naga では型（`Type`）の重複排除に使われる。
- **`Handle<T>`**: 単なる整数ラッパーで、`Copy` 可能・軽量・型付き。「型情報を保ったまま安全に配列を指せるインデックス」という位置づけ。

この設計を採用する理由（naga から借用する動機）:

- 木構造やグラフ構造をポインタ／参照で表現すると、循環参照や所有権の管理が複雑になる（C# でも `class` 参照でグラフを組むと GC・等価性・シリアライズが煩雑になりがち）。Arena + Handle なら全ノードがフラットな配列に収まり、参照は単なる整数になる
- `Handle<T>` は値型・軽量なので、辞書検索なしで O(1) アクセスができる
- IR 全体が「配列の集合」になるため、シリアライズ・デシリアライズやテストでの構造比較が容易になる
- HLSL のようにテキストベースで前方参照が制限された言語では、宣言順（＝配列に格納される順）がそのまま依存関係の順序になるという性質が活かせる（詳細は §2.3）

TkHLSL でも、リソース宣言・関数・型を **`Arena<T>` / `UniqueArena<T>` / `Handle<T>`** で管理する設計を採用します（詳細は §5, §6）。

### 2.2 Module という中心的な IR

naga はパース結果を `Module` という単一の構造体に集約します（[`naga/src/ir/mod.rs`](https://github.com/gfx-rs/wgpu/blob/trunk/naga/src/ir/mod.rs)）。抜粋:

```rust
pub struct Module {
    pub types: UniqueArena<Type>,
    pub constants: Arena<Constant>,
    pub global_variables: Arena<GlobalVariable>,
    pub functions: Arena<Function>,
    pub entry_points: Vec<EntryPoint>,
    // ...
}
```

重要なのは、**`Function`（通常の関数）と `EntryPoint`（エントリポイント）を明確に区別している**点です。

```rust
pub struct EntryPoint {
    pub name: String,
    pub stage: ShaderStage,
    pub workgroup_size: [u32; 3],
    pub function: Function,
    // ...
}
```

`Function` はシェーダー内のどこからでも呼び出されうる通常の関数、`EntryPoint` はステージ（vertex/fragment/compute）ごとのエントリポイントで、compute の場合は `workgroup_size`（HLSL でいう `[numthreads(x,y,z)]`）を持ちます。この区別は TkHLSL の「ヘルパー関数」と「カーネル関数」の区別にそのまま対応します。

また `GlobalVariable` はリソース宣言を表します:

```rust
pub struct GlobalVariable {
    pub name: Option<String>,
    pub space: AddressSpace,
    pub binding: Option<ResourceBinding>,
    pub ty: Handle<Type>,
    // ...
}
```

`space`（WGSL のアドレス空間: uniform/storage/handle など）と `binding`（`@group(N) @binding(M)` 相当のスロット情報）を持ちます。HLSL には WGSL のような抽象的なアドレス空間の概念はなく、`StructuredBuffer`/`Texture2D`/`SamplerState` のような具体的なリソース型がそのままリソース種別になるため、TkHLSL では `space` の代わりに `ResourceKind` enum（具体的な HLSL リソース型）を持たせます。`binding`（`register(t0)` 相当）はそのまま対応する概念として採用します。

さらに Module の重要な不変条件として、naga のドキュメントコメントには次のようにあります:

> Each function must appear in this arena strictly before all its callers. Recursion is not supported.

つまり **`functions` 配列は「呼び出し先が呼び出し元より必ず前に来る」順序（トポロジカル順）で格納されている**ことが前提になっています。これは HLSL 自体の制約（関数は使用箇所より前で宣言・定義されている必要があり、再帰は未対応）と自然に一致するため、TkHLSL でも同じ不変条件をそのまま採用できます（§8 Phase 3, Phase 4 参照）。

### 2.3 Analyzer: 「どの関数がどのグローバルを使うか」を1パスで解決する

naga の `valid::analyzer`（[`naga/src/valid/analyzer.rs`](https://github.com/gfx-rs/wgpu/blob/trunk/naga/src/valid/analyzer.rs)）は、まさに TkHLSL の第一フェーズのゴールと同じ問題を解いています。関数ごとに `FunctionInfo` を計算し、その中に次のフィールドを持ちます:

```rust
pub struct FunctionInfo {
    // ...
    /// How this function and its callees use this module's globals.
    pub global_uses: Box<[GlobalUse]>,
    // ...
}
```

`global_uses` は `Handle<GlobalVariable>` でインデックスされた配列で、「その関数（および呼び出す関数全て）がどのグローバル変数をどう使うか」を表します。これは TkHLSL でいう「カーネルが使用するリソース一覧」そのものです。

この `global_uses` の計算方法が重要な設計上のヒントになります。naga のバリデータ（[`naga/src/valid/mod.rs`](https://github.com/gfx-rs/wgpu/blob/trunk/naga/src/valid/mod.rs)）は次のように動作します:

```rust
for (handle, fun) in module.functions.iter() {
    // fun を検証し FunctionInfo を計算して mod_info.functions に積む
}
for ep in module.entry_points.iter() {
    // ep を検証し FunctionInfo を計算して mod_info.entry_points に積む
    // （この時点で呼び出し先の FunctionInfo は既に計算済み）
}
```

`module.functions` は §2.2 の不変条件により「呼び出し先が呼び出し元より前」に並んでいるため、**先頭から順に1回スキャンするだけで、関数呼び出し（`process_call`）の際に呼び出し先の `FunctionInfo.global_uses` をそのまま自分の集合にマージできます**。グラフを都度たどる DFS/BFS を各エントリポイントごとに個別実行する必要はなく、全体で O(関数数 + 参照数) の単一パスで完結します。循環呼び出しも構造的に発生しません（HLSL 自体が再帰非対応のため）。

TkHLSL でもこの設計をそのまま採用し、「関数本体の識別子スキャン」と「呼び出しグラフ解決」を**別々の2フェーズに分けず、naga の Analyzer のような単一パスの解析フェーズに統合**します（旧案では Phase 4「関数本体の軽量スキャン」と Phase 5「呼び出しグラフ解決」に分けていましたが、本改訂でこれらを Phase 4「Analyzer」に統合しました。詳細は §8）。

### 2.4 採用する要素・採用しない要素

| naga の要素 | TkHLSL での扱い |
|---|---|
| `Arena<T>` / `UniqueArena<T>` / `Handle<T>` | **採用**。リソース宣言・関数・型の格納に使う（§5, §6） |
| `Module`（`types`/`global_variables`/`functions`/`entry_points`） | **採用**（概念のみ）。TkHLSL の内部 IR として同じ形を持たせる |
| `Function` と `EntryPoint` の分離 | **採用**。ヘルパー関数とカーネル関数を型レベルで区別する |
| 「呼び出し先は呼び出し元より arena 内で前」という不変条件 | **採用**。HLSL の非再帰・前方宣言必須という性質と合致するため活かす |
| Analyzer の `global_uses` 単一パス計算 | **採用**。旧 Phase4+5 を統合する形で第一フェーズの中核ロジックにする |
| `AddressSpace`（WGSL 的な抽象アドレス空間） | **不採用**。HLSL の具体的なリソース型をそのまま `ResourceKind` として列挙する |
| Uniformity 解析（制御フローの一様性チェック） | **不採用**（将来検討）。コード生成を行わない TkHLSL の第一フェーズでは不要 |
| texture/sampler ペア解析（`sampling_set`） | **不採用**（将来検討）。§11 将来ロードマップに記載 |
| 定数畳み込み（`proc/constant_evaluator.rs`） | **不採用**（非ゴール）。式のフル評価は行わない |
| 複数フロントエンド（WGSL/GLSL/SPIR-V） | **不採用**。TkHLSL は HLSL 専用 |
| バックエンド（コード生成、`back/*`） | **不採用**。C# バインディングコード生成は外部の Roslyn ライブラリの責務 |
| `proc::Namer`（識別子衝突回避、コード生成向け） | **不採用**。TkHLSL はコード生成をしないため不要 |

## 3. 用語集

| 用語 | 説明 |
|---|---|
| Arena | 値の配列。`Handle` によってインデックスされる（naga 由来、§2.1） |
| Handle\<T\> | Arena/UniqueArena 内の要素を指す、型付きの軽量なインデックス（naga 由来） |
| Module | Types・GlobalVariables・Functions・EntryPoints をまとめた TkHLSL の内部 IR ルート（naga 由来） |
| GlobalVariable | トップレベルのリソース宣言（バッファ・テクスチャ・サンプラー・cbuffer 等）を表す IR ノード |
| Function | カーネルから（直接・間接に）呼ばれるヘルパー関数を表す IR ノード |
| EntryPoint / Kernel | `#pragma kernel` + `[numthreads(x,y,z)]` で宣言されるエントリポイント関数 |
| FunctionInfo | Analyzer が関数・エントリポイントごとに算出する解析結果（使用する GlobalVariable の集合など） |
| ModuleInfo | Module 全体に対する `FunctionInfo` の集約結果 |
| Resource Binding | カーネルが使用するリソース参照情報（公開 API 上の表現） |
| register slot | `: register(t0)` のようにリソースに明示的に割り当てられたスロット指定 |
| トリビア | コメント・空白などトークンの意味に影響しない付随情報 |
| ゴールデンコーパス | 回帰テスト用に用意する代表的な HLSL サンプル集 |

## 4. ゴール／非ゴール

### ゴール（第一フェーズで到達する範囲）

- HLSL ソースからカーネル宣言（`#pragma kernel` + `[numthreads]` 関数、IR 上の `EntryPoint`）を検出する
- トップレベルのリソース宣言（`StructuredBuffer<T>`, `RWStructuredBuffer<T>`, `Texture2D` 系, `RWTexture2D` 系, `cbuffer`, `SamplerState` など、IR 上の `GlobalVariable`）を解析する
- 各カーネル（`EntryPoint`）が、直接・間接（ヘルパー `Function` 経由）に、どの `GlobalVariable` を参照しているかを、naga の Analyzer に倣った単一パスの解析で特定する
- 上記を、カーネルごとの「設定すべきリソース一覧」として公開データモデルにまとめる

### 非ゴール（将来フェーズ、または対象外）

- 式・制御フローを含むフル HLSL 文法解析（if/for などのパース）
- 高度な診断・エラー回復（IDE 向け赤線表示レベルの品質）
- `multi_compile` / `shader_feature` によるバリアントマトリクスの展開
- register/space の自動割当・重複検証
- 型チェック・意味解析（型の整合性検証など）、定数畳み込み
- naga のような制御フロー一様性（uniformity）解析、texture/sampler ペア解析
- 複数シェーダー言語のフロントエンド対応（TkHLSL は HLSL 専用）
- コード生成（バックエンド）— 外部の Roslyn ライブラリの責務
- IDE 向けの増分解析（TkHLSL はバッチ一発解析のみを想定）
- ローカル変数によるグローバル識別子のシャドーイング検出

## 5. 全体アーキテクチャ

```
Source Text
  → Lexer                       生トークン列
  → Preprocessor                 最終トークン列（マクロ展開・条件コンパイル解決済み）
  → Parser (TopLevelParser)       Module IR を構築
                                    - Types: UniqueArena<TypeInfo>
                                    - GlobalVariables: Arena<GlobalVariable>
                                    - Functions: Arena<Function>（本体はトークン範囲のみ）
                                    - EntryPoints: List<EntryPoint>（本体はトークン範囲のみ）
  → Analyzer                     Module を1パス解析し ModuleInfo を構築
                                    - Functions を宣言順（＝呼び出し依存順）にスキャンし、
                                      FunctionInfo.GlobalUses を算出しながら呼び出し先の
                                      FunctionInfo をマージ（callグラフ解決を同時に行う）
                                    - EntryPoints も同様に解析
  → 公開モデル構築                Module + ModuleInfo を合成し HlslCompilationResult を返す
```

TkHLSL はソーステキスト全体を一度に解析する**バッチ処理**であり、IDE 向けの増分解析（部分再パース）は行いません。naga との対応関係でいうと、TkHLSL は `front`（フロントエンド）と `valid`（検証・解析）に相当する部分のみを実装し、`back`（バックエンド、コード生成）に相当する部分は実装しません。

## 6. プロジェクト構成方針

当面は単一 csproj（`TkHLSL/TkHLSL.csproj`）＋フォルダ・名前空間分割で進めます。複数プロジェクトへの物理分割は、依存関係管理コストが実利を上回るため時期尚早と判断します。

```
TkHLSL/
├── Arena/           Handle<T>, Arena<T>, UniqueArena<T>（naga の arena.rs 相当の基盤）
├── Lexing/          Lexer, Token, TokenKind
├── Preprocessing/   Preprocessor, IIncludeResolver, HlslParseOptions
├── Ir/              Module, GlobalVariable, EntryPoint, Function, TypeInfo, ResourceKind（naga の ir.rs 相当の IR 定義）
├── Syntax/          TopLevelParser（トークン列から Ir/* を構築するパーサー本体）
├── Analysis/        Analyzer, FunctionInfo, ModuleInfo（naga の valid/analyzer.rs 相当）
├── Model/           HlslCompilationResult, KernelBindingInfo, ResourceBinding（外部公開用の平易化モデル）
└── HlslParser.cs    公開エントリポイント

tests/
└── TkHLSL.Tests/    xUnit, net10.0
    └── Fixtures/    サンプル .compute ファイル群
```

将来、外部 Analyzer リポジトリが「パース結果の型定義だけ」を軽量に参照したくなった場合は、`TkHLSL.Abstractions`（`Ir/`・`Model/` 相当の POCO のみ、パーサー本体なし）の切り出しを検討します（§11 拡張ロードマップ）。

## 7. 公開 API とデータモデル

### 7.1 内部 IR（naga 由来の命名を採用）

- `Handle<T>` — `Arena/Handle.cs`。整数インデックスを型でラップした軽量な値型（`readonly struct`）
- `Arena<T>` / `UniqueArena<T>` — `Arena/Arena.cs`, `Arena/UniqueArena.cs`
- `Module` — `Ir/Module.cs`。`Types: UniqueArena<TypeInfo>`, `GlobalVariables: Arena<GlobalVariable>`, `Functions: Arena<Function>`, `EntryPoints: IReadOnlyList<EntryPoint>`, `Diagnostics`
- `GlobalVariable` — `Ir/GlobalVariable.cs`。`Name`, `Kind: ResourceKind`, `ElementType: Handle<TypeInfo>?`, `Register: ResourceRegister?`（`register(tN)` 等）, `Location`
- `Function` — `Ir/Function.cs`。`Name`, `BodyTokenRange`, `Location`（本体はトークン範囲のみ、Phase 3 では中身をパースしない）
- `EntryPoint` — `Ir/EntryPoint.cs`。`Name`, `ThreadGroupSize (X, Y, Z)`, `Function`, `Location`
- `ResourceKind` — enum（StructuredBuffer, RWStructuredBuffer, Texture2D, RWTexture2D, SamplerState, CBuffer, PlainGlobal, ...）。naga の `AddressSpace` に相当するが、HLSL の具体的なリソース型をそのまま列挙する（§2.4 参照）

### 7.2 解析結果（naga の ModuleInfo/FunctionInfo 由来の命名を採用）

- `ModuleInfo` — `Analysis/ModuleInfo.cs`。`Functions: IReadOnlyList<FunctionInfo>`, `EntryPoints: IReadOnlyList<FunctionInfo>`
- `FunctionInfo` — `Analysis/FunctionInfo.cs`。`GlobalUses: IReadOnlySet<Handle<GlobalVariable>>`。naga の `FunctionInfo` と異なり、`Uniformity`/`sampling_set`/`may_kill` などコード生成向けの情報は持たない（§2.4 の非採用項目）

### 7.3 公開 API（外部コンシューマ向けの平易化モデル）

```csharp
public static class HlslParser
{
    public static HlslCompilationResult Parse(string sourceText, HlslParseOptions options);
}
```

- `HlslCompilationResult` — `Model/HlslCompilationResult.cs`。`Kernels: IReadOnlyList<KernelBindingInfo>`, `AllResources`（未使用リソースも含む全件、将来の未使用警告用）, `Diagnostics`
- `KernelBindingInfo` — `Model/KernelBindingInfo.cs`。`Name`, `ThreadGroupSize`, `Bindings: IReadOnlyList<ResourceBinding>`, `Location`
- `ResourceBinding` — `Model/ResourceBinding.cs`。`Name`, `ResourceKind`, `ElementTypeName`, `ExplicitRegister?`, `Location`

`Module`/`ModuleInfo`（内部 IR ＋解析結果）と `HlslCompilationResult`（外部公開モデル）を分離しているのは、内部 IR は Handle ベースで効率重視、公開 API は外部コンシューマが扱いやすい平易な形（名前ベース・フラットな一覧）にするためです。`HlslParser.Parse` の内部で `Module` → `Analyzer` → `ModuleInfo` → `HlslCompilationResult` への変換を行います。

全モデルはイミュータブル・値等価性重視（`record` 型を基本とする）。Diagnostic は独自の軽量型を持ち、Roslyn の `Diagnostic` 型には依存しません。これは「TkHLSL は Roslyn を知らないプレーンなパーサーライブラリである」という方針をコードレベルで担保する重要な設計原則です。

## 8. フェーズ一覧

| # | フェーズ名 | 目的 |
|---|---|---|
| 0 | 基盤整備 | プロジェクト構成・TFM 方針・テストプロジェクト・Arena/Handle 基盤の土台作り |
| 1 | Lexer（字句解析） | 生 HLSL テキスト → トークン列 |
| 2 | Preprocessor | `#pragma`/`#define`/`#if`/`#include` 処理、最終トークン列生成 |
| 3 | パーサー（Module 構築） | トークン列 → `Module` IR（リソース宣言・struct・カーネル/関数シグネチャ。本体はトークン範囲のみ） |
| 4 | Analyzer（GlobalUses 解析） | naga 流の単一パスで各 `Function`/`EntryPoint` の `GlobalUses` を算出（旧案の「関数本体スキャン」＋「呼び出しグラフ解決」を統合） |
| 5 | 公開モデル構築（公開API） | `Module` + `ModuleInfo` を合成し `HlslCompilationResult` を確定、`Class1.cs` を置換 |
| 6 | 統合テスト・ゴールデンコーパス・パッケージング準備 | サンプル HLSL 集、回帰テスト、CI、netstandard2.0 対応の最終判断 |

## 9. フェーズごとの詳細仕様

### Phase 0: 基盤整備

- **入力**: 現状の空リポジトリ
- **タスク**:
  - `TkHLSL/TkHLSL.csproj` に `<LangVersion>` を明示指定（TFM のデフォルト LangVersion に依存させない）
  - フォルダ構成を作成: `Arena/`, `Lexing/`, `Preprocessing/`, `Ir/`, `Syntax/`, `Analysis/`, `Model/`
  - `Arena/Handle.cs`, `Arena/Arena.cs`, `Arena/UniqueArena.cs` の基盤実装（§7.1）。`Handle<T>` は `readonly struct`、`Arena<T>` は内部 `List<T>` ラッパーで、`Handle<T> Add(T item)` と `T this[Handle<T> handle]` を提供する最小実装から始める
  - テストプロジェクト `tests/TkHLSL.Tests/TkHLSL.Tests.csproj`（xUnit, net10.0）を作成し `.sln` に追加
  - `tests/Fixtures/` フォルダを用意（中身は後続フェーズで追加）
  - netstandard2.0 マルチターゲット化を「今やるか Phase 6 まで先送りするか」の意思決定ポイントとして扱う（§12 参照）
- **成果物**: 更新済み `.sln`/`.csproj` 群、フォルダスケルトン、`Arena/*` の最小実装
- **DoD**: `dotnet build` 成功、`dotnet test`（空でも）実行可能、`Arena<T>`/`Handle<T>` の基本操作（追加・取得・列挙）にユニットテストが通る

### Phase 1: Lexer

- **入力**: HLSL ソーステキスト（`string`）
- **出力**: `Token` 列（種別・値・位置情報。プリプロセッサ行判定用の改行トークン・`#` 行トークンも含む）
- **対象**: 識別子、数値リテラル（int/float/hex、`f`/`u`/`h` サフィックス）、文字列リテラル（`#include` パス用）、記号一式、行/ブロックコメント（トリビアとして保持）
- **設計判断**: キーワード表は Lexer に持たせず汎用トークンのみ扱う（キーワード解釈は Syntax 層に委譲）。不正入力は例外を投げず Diagnostic＋エラートークンで表現する（後続フェーズ共通の例外方針）
- **成果物**: `Lexing/Lexer.cs`, `Lexing/Token.cs`, `Lexing/TokenKind.cs`, 位置情報型
- **DoD**: 代表 HLSL サンプルのトークン化結果がユニットテストで期待通り。未終端文字列等の異常系がクラッシュせず Diagnostic を返す

### Phase 2: Preprocessor

- **入力**: Phase 1 の生トークン列
- **出力**: マクロ展開・条件コンパイル解決済みの最終トークン列
- **対応ディレクティブ**:
  - `#pragma kernel Name` → カーネル候補名として記録（実在確認は Phase 3）
  - `#pragma multi_compile` / `#pragma shader_feature` → バリアント展開せず無視（既知の制限）
  - `#define NAME value`（オブジェクトマクロのみ、関数マクロは非対応）、`#undef`
  - `#if defined(X)` / `#ifdef` / `#ifndef` / `#else` / `#elif` / `#endif` — 呼び出し側が渡す `DefinedSymbols` 集合に基づき分岐解決。デフォルトは空集合（単一バリアントのみ解析）
  - `#include "path"` — ファイル I/O は TkHLSL の責務外とし `IIncludeResolver` インターフェースをホスト側実装に委譲
- **成果物**: `Preprocessing/Preprocessor.cs`, `Preprocessing/IIncludeResolver.cs`, `Preprocessing/HlslParseOptions.cs`
- **DoD**: 代表的なプリプロセッサパターンで期待通りのトークン列/カーネル列挙になる。未定義シンボルでの `#ifdef` ブロック除外が動作する

### Phase 3: パーサー（Module 構築）

- **入力**: Phase 2 の最終トークン列
- **出力**: `Module`（§7.1）。リソース宣言は `GlobalVariable` として `Arena<GlobalVariable>` に、struct は `TypeInfo` として `UniqueArena<TypeInfo>` に、カーネル/ヘルパー関数は `EntryPoint`/`Function` として `Arena<Function>`・`List<EntryPoint>` に追加する。**関数本体はトークン範囲のみ記録し、パースしない**（波括弧の深さカウントでスキップ）
- **重要な不変条件**: `Functions` は「呼び出し先が呼び出し元より前に来る」順序で構築する。HLSL は前方宣言なしに未定義の関数を呼べないため、ソースの出現順にそのまま `Arena` へ追加すれば自然にこの不変条件が満たされる（§2.2 参照。プロトタイプ宣言のみ先に書き実体を後で定義するパターンは既知の制限として扱う）
- **対応宣言**:
  - リソース: `Texture2D/2DArray/3D/Cube/CubeArray`, `RWTexture2D/2DArray/3D`, `StructuredBuffer<T>`, `RWStructuredBuffer<T>`, `Append/ConsumeStructuredBuffer<T>`, `ByteAddressBuffer`/`RWByteAddressBuffer`, `ConstantBuffer<T>`, `SamplerState`/`SamplerComparisonState`。ジェネリック型引数・任意の `: register(tN/uN/bN/sN[, spaceM])`・任意の配列添字を許容
  - `struct Name { ... };`、`cbuffer Name [: register(bN)] { ... }`
  - カーネル関数: `[numthreads(x,y,z)]` 属性＋シグネチャ＋本体トークン範囲 → `EntryPoint`
  - ヘルパー関数: 同様にシグネチャ＋本体トークン範囲（`numthreads` なし）→ `Function`
  - cbuffer 外の単純グローバル変数はリソースと区別して記録（暗黙 cbuffer 変数の扱いは将来課題）
- **成果物**: `Ir/Module.cs`, `Ir/GlobalVariable.cs`, `Ir/Function.cs`, `Ir/EntryPoint.cs`, `Ir/TypeInfo.cs`, `Syntax/TopLevelParser.cs`
- **DoD**: 主要リソース種別を網羅したフィクスチャで `Module` の内容が期待通り。構文エラー時は該当宣言をスキップし Diagnostic 化して処理継続。関数本体のトークン範囲が正しく取得できる。`Functions` の並び順が呼び出し依存順になっていることをテストで確認する

### Phase 4: Analyzer（GlobalUses 解析）

naga の `valid::analyzer`（§2.3）に倣い、旧案の「関数本体の軽量スキャン」と「呼び出しグラフ解決」を1つのフェーズに統合する。

- **入力**: Phase 3 の `Module`
- **出力**: `ModuleInfo`。`Module.Functions` の各要素に対応する `FunctionInfo`（`Functions` と同じ順で並ぶ）、および `Module.EntryPoints` の各要素に対応する `FunctionInfo` のリスト
- **アルゴリズム**:
  1. `Module.Functions` を先頭から順にスキャンする（Phase 3 の不変条件により、この時点で呼び出し先は必ず処理済み）
  2. 各関数の本体トークン範囲を線形スキャンし、識別子トークンを `GlobalVariables`/`Functions` の名前テーブルと突合する
     - 識別子が `GlobalVariable` 名と一致すれば `GlobalUses` に直接追加
     - 識別子直後が `(` で `Function` 名と一致すれば、その呼び出し先の（既に計算済みの）`FunctionInfo.GlobalUses` を自分の `GlobalUses` にマージする（`mul`/`dot`/`Sample`/`Load` 等の組込み関数は名前テーブルに存在しないため自然に除外される）
     - `tex.Sample(sampler, uv)` のようなメンバアクセスは先頭識別子をリソース参照として検出し、メソッド名側は無視する
  3. `Module.EntryPoints` についても同様の手順で `FunctionInfo` を計算する（この時点で全ヘルパー関数の `FunctionInfo` は計算済みなので、呼び出しグラフを辿る追加のパスは不要）
- **既知の制限**: ローカル変数によるグローバル識別子のシャドーイングは検出しない。未解決の呼び出し（存在しない関数名）は Warning Diagnostic として記録し処理を継続する
- **成果物**: `Analysis/Analyzer.cs`, `Analysis/FunctionInfo.cs`, `Analysis/ModuleInfo.cs`
- **DoD**: 3 階層以上の呼び出しチェーンでリソース参照が正しく伝播する。単一パスのみで解析が完結すること（各カーネルごとに個別のグラフ探索を行わないこと）をテストまたはコードレビューで確認する

### Phase 5: 公開モデル構築（公開API）

- **入力**: Phase 3 の `Module`＋Phase 4 の `ModuleInfo`
- **出力**: §7.3 に記載の公開 API 一式（`HlslParser.Parse` が `Class1.cs` を置換）。`Module.EntryPoints[i]` と `ModuleInfo.EntryPoints[i]`（同じ添字で対応）を突合し、`KernelBindingInfo` に変換する
- **成果物**: `Model/*.cs` 一式、`HlslParser.cs`
- **DoD**: 単一カーネル／複数カーネル+リソース共有／多階層ヘルパー／テクスチャ+サンプラ+cbuffer 混在の各パターンで E2E テストが green。主要公開型に XML ドキュメントコメント付与済み

### Phase 6: 統合テスト・ゴールデンコーパス・パッケージング準備

- **タスク**:
  - `tests/Fixtures/*.compute` を 10〜15 本程度整備（代表パターンを自作）
  - コーパス全件に対する回帰テスト
  - CI（GitHub Actions 等）の設定検討
  - Phase 0 で先送りしていた場合、ここで netstandard2.0 マルチターゲット化を最終実施
  - NuGet パッケージメタデータの項目だけ用意（公開判断自体は別途）
- **DoD**: コーパス全件 green、（対応していれば）両 TFM ビルド green

## 10. サンプル HLSL／テストフィクスチャ戦略

`tests/Fixtures/` にサンプルを配置し、以下のパターンを最低限カバーする:

- 単一カーネル、単一バッファ
- 複数カーネルでバッファを共有するケース
- ヘルパー関数を多階層で呼び出すケース（kernel → helperA → helperB → リソース参照）
- Texture + Sampler + cbuffer が混在するケース
- ローカル変数によるシャドーイング（既知の制限を示すためのサンプル。期待結果として「誤検出する」ことをテストで明示する）
- ネストした `#if`/`#ifdef`

## 11. テスト戦略

- xUnit を採用
- フェーズ単位でユニットテストを DoD に含める（各フェーズの成果物ごとに専用テストクラスを用意）
- Phase 6 で `tests/Fixtures/` のコーパス全件に対する統合・回帰テストを追加

## 12. 将来フェーズ／拡張ロードマップ

- フル式・制御フロー文法解析（if/for などの本格的な AST 化）
- 診断・エラー回復の強化（IDE 向け品質）
- `multi_compile`/`shader_feature` バリアントマトリクスの解決
- 増分パース（IDE 向け）
- register/space の自動検証
- 型チェック、定数畳み込み（naga の `proc/constant_evaluator.rs` に相当）
- テクスチャ・サンプラーのペア解析（naga の `sampling_set` に相当。`Texture2D.Sample(SamplerState, ...)` の組み合わせ検出）
- パフォーマンス最適化
- `TkHLSL.Abstractions`（`Ir/`・`Model/` のみを含む軽量パッケージ）の切り出し

## 13. オープン課題・要検討事項

- **netstandard2.0 マルチターゲット化のタイミング**: 将来、外部 Roslyn Analyzer/Source Generator プロジェクトが TkHLSL を参照する際、Analyzer/Generator プロジェクトは netstandard2.0 をターゲットにする必要があるため、TkHLSL 自身も netstandard2.0 でビルド可能でなければ参照できない。
  - → **決定（Phase 0 で対応）**: 案Aを採用。`TkHLSL/TkHLSL.csproj` を `<TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks>` にマルチターゲット化済み。現状の `Arena`/`Handle` 実装は netstandard2.0 の BCL のみで警告なくビルドできている。以後のフェーズで netstandard2.0 に存在しない API（`IsExternalInit`/`required`/`NotNullWhen` 等）を使う場合は都度ポリフィル（`PolySharp` 等）の要否を検討する
- **LangVersion 既定値の罠**: `TargetFramework=netstandard2.0` の場合、明示指定しない限り LangVersion が暗黙的に古いバージョン（C# 7.3 相当）に固定される。record 型やパターンマッチ等を使う場合は `<LangVersion>` の明示指定が必須
- **BCL ギャップとポリフィル**: netstandard2.0 には `IsExternalInit`, `required` 属性, `NotNullWhen` 等が存在しない。`PolySharp`（ランタイム依存なしのソースジェネレータ型ポリフィル）の採用を検討
- **依存パッケージの最小化**: `System.Memory`/`System.Text.Json` 等の追加パッケージ導入は、Analyzer NuGet パッケージ内でのアセンブリバージョン競合リスクを増やすため、可能な限り避ける
- **カルチャ非依存性**: 文字列比較は `Ordinal` 系比較を用いる
- **`Handle<T>`/`Arena<T>` の実装形態**: `Handle<T>` は `readonly struct`（値型・軽量）とする。`Arena<T>` を `class`（参照型）にするか `struct` にするかは要検討（naga の Rust 実装は所有権の都合で `Arena<T>` 自体は値だが `Module` に埋め込まれる形。C# では `Module` を `class` にして `Arena<T>` フィールドを持たせるのが自然）
- **テストプロジェクトは対象外**: `tests/TkHLSL.Tests` は net10.0 のみでよく、マルチターゲット化はライブラリ本体のみに適用する
- **Unity 組込み include の扱い**: `UnityCG.cginc` 等をバンドルするか、`IIncludeResolver` を通じて完全に外部委譲のままにするかは未決定
