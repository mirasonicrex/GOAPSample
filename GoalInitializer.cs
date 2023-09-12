using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

namespace NPCS
{
    [Serializable]
    public class GoalDefinition
    {
        public string key;
        public bool value;
        public bool isRemovable;
        public float priority;
       // public Attribute relatedAttribute; 
       // public Attribute relatedDesire; 
    }

    public class GoalInitializer : ScriptableObject
    {
        public GoalDefinition goalDefinition;
    }
}