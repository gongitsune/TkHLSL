using System;
using TkHLSL.Unity;
using UnityEngine;

namespace Features.Fluid.Scripts
{
    [ComputeShaderBinding(
        "Assets/Features/Fluid/Computes/MPM.compute"
    )]
    public partial class FluidCompute
    {
    
    }

    [ComputeShaderBinding(
        "Assets/Features/Fluid/Computes/Marching Cubes.compute"
    )]
    public partial class McCompute
    {
        
    }

    [Serializable]
    public class T
    {
        [SerializeField] private ComputeShader shader;
        
        public T()
        {
            var compute = new FluidCompute(shader);
        }
    }
}