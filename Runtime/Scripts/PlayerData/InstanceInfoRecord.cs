﻿using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MeshSync
{
    [Serializable]
    internal class InstanceInfoRecord
    {
        public GameObject go;
        public MeshSyncInstanceRenderer renderer;
        public List<GameObject> instanceObjects = new List<GameObject>();
    }
}