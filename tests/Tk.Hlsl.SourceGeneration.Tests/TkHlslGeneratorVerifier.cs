using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.CodeAnalysis.Testing.Verifiers;
using Tk.Hlsl.SourceGeneration;

namespace Tk.Hlsl.SourceGeneration.Tests;

/// <summary>
///     Thin wrapper around <see cref="CSharpSourceGeneratorTest{TSourceGenerator, TVerifier}" /> that
///     wires up what every <see cref="ComputeShaderBindingGenerator" /> test needs: a reference to
///     <c>Tk.Hlsl.Unity</c> for <c>[ComputeShaderBinding]</c>, a minimal <c>UnityEngine</c> stub (see
///     <see cref="UnityStub" />) so generated code referencing <c>ComputeShader</c>/<c>Texture</c>/etc.
///     actually compiles, and one or more <c>AdditionalFiles</c> for the <c>.compute</c> source(s) — see
///     docs/IMPLEMENTATION_PLAN.md §9 Phase 9.5.
/// </summary>
internal static class TkHlslGeneratorVerifier
{
    public static Test Create(string userSource, params (string Path, string Content)[] additionalFiles)
    {
        var test = new Test
        {
            TestState =
            {
                Sources = { userSource, UnityStub.Source }
            },
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
            // These tests only care that the generator's output actually compiles against
            // UnityStub — they don't pin the exact generated text (see GeneratorDriverTests for
            // that), so skip the framework's "generated file list matches exactly" comparison.
            TestBehaviors = TestBehaviors.SkipGeneratedSourcesCheck
        };

        foreach (var (path, content) in additionalFiles)
            test.TestState.AdditionalFiles.Add((path, content));

        test.TestState.AdditionalReferences.Add(
            typeof(Unity.ComputeShaderBindingAttribute).Assembly);

        return test;
    }

    public sealed class Test : CSharpSourceGeneratorTest<ComputeShaderBindingGenerator, XUnitVerifier>
    {
        protected override IEnumerable<Type> GetSourceGenerators()
        {
            yield return typeof(ComputeShaderBindingGenerator);
        }
    }
}
