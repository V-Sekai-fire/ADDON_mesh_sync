﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MeshSync
{
    public class MeshInstanceInfo
    {
        public Mesh Mesh;
        
        public List<Matrix4x4[]> DividedInstances { get; private set; } = new List<Matrix4x4[]>();
        public Material[] Materials;

        private GameObject m_gameObject;
        private Matrix4x4 m_inverse = Matrix4x4.identity;

        private Transform m_root;

        private bool m_dirtyInstances = true;
        
        private Matrix4x4 m_cachedWorldMatrix = Matrix4x4.zero;
        
        private Matrix4x4[] m_instances;
        
        public Transform Root
        {
            get => m_root;
            set
            {
                m_root = value;
                UpdateInverse();
            }
        }

        public Matrix4x4[] Instances
        {
            get => m_instances;
            set
            {
                m_instances = value;
                m_dirtyInstances = true;
            }
        }
        
        public GameObject GameObject
        {
            get => m_gameObject;
            set
            {
                m_gameObject = value;
               UpdateInverse();
            }
        }

        private void UpdateInverse()
        {
            if (m_gameObject == null || Root == null)
            {
                m_inverse = Matrix4x4.identity;
            }
            else
            {
                m_inverse = m_gameObject.transform.localToWorldMatrix.inverse * Root.localToWorldMatrix;
            }
        }
        

        public Renderer Renderer;
        public int Layer
        {
            get
            {
                if (GameObject == null) 
                    return 0;
                    
                return GameObject.layer; 
            } 
        }

        public bool ReceiveShadows
        {
            get
            {
                if (Renderer == null)
                    return true;
                    
                return Renderer.receiveShadows;
            }
        }

        public ShadowCastingMode ShadowCastingMode
        {
            get
            {
                if (Renderer == null)
                {
                    return ShadowCastingMode.On;
                }

                return Renderer.shadowCastingMode;
            }
        }

        public LightProbeUsage LightProbeUsage
        {
            get
            {
                if (Renderer == null)
                {
                    return LightProbeUsage.BlendProbes;
                }

                return Renderer.lightProbeUsage;
            }
        }

        public LightProbeProxyVolume LightProbeProxyVolume
        {
            get
            {
                if (Renderer == null)
                    return null;
                
                if (Renderer.lightProbeUsage != LightProbeUsage.UseProxyVolume)
                    return null;
                
                // Look for component in override
                var overrideGO = Renderer.lightProbeProxyVolumeOverride;
                if (overrideGO != null && overrideGO.TryGetComponent(out LightProbeProxyVolume volumeOverride))
                {
                    return volumeOverride;
                }
                
                // Look for component in Renderer
                if (Renderer.TryGetComponent(out LightProbeProxyVolume volume))
                {
                    return volume;
                }

                return null;
            }
        }
        
        public Matrix4x4 WorldMatrix
        {
            get
            {
                if (GameObject == null)
                    return Matrix4x4.identity;
                    
                return GameObject.transform.localToWorldMatrix * m_inverse;
            }
        }
            
        public void UpdateDividedInstances()
        {
            // Avoid recalculation if the instances are the same
            // and the world matrix has not changed.
            if (!m_dirtyInstances && m_cachedWorldMatrix == WorldMatrix)
                return;
            
            m_dirtyInstances = false;
            m_cachedWorldMatrix = WorldMatrix;
            
            DividedInstances.Clear();
            
            if (Instances == null)
                return;
                
            var maxSize = 1023;
            var iterations = Instances.Length / maxSize;
            var worldMatrix = WorldMatrix;
            for (var i = 0; i < iterations; i++)
            {
                var array = new Matrix4x4[maxSize];

                for (var j = 0; j < array.Length; j++)
                {
                    array[j] = worldMatrix * Instances[i * maxSize + j];
                }
                
                DividedInstances.Add(array);
            }

            var remainder = Instances.Length % maxSize;
            if (remainder > 0)
            {
                var array = new Matrix4x4[remainder];
                
                for (var j = 0; j < array.Length; j++)
                {
                    array[j] = worldMatrix * Instances[iterations * maxSize + j];
                }
                
                DividedInstances.Add(array);
            }
        }
    }
}