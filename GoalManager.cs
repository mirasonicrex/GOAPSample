using System;
using System.Collections.Generic;
using System.Linq;
using Characteristics;
using NPCS.Schedule.Goap;
using Sirenix.OdinInspector;
using UnityEngine;
using Attribute = Characteristics.Attribute;

namespace NPCS
{
    [Serializable]
    public class Goal 
    {
        public KeyValuePair<string, object> GoalState { get; private set; }
        public bool IsRemovable { get; private set; }
        public float Priority { get; set; }
        
        public Attribute RelatedAttribute { get; private set; }

        public Goal(string key, object value, bool isRemovable, float priority, Attribute relatedAttribute)
        {
            GoalState = new KeyValuePair<string, object>(key, value);
            IsRemovable = isRemovable;
            Priority = priority;
            RelatedAttribute = relatedAttribute;
        }
        
    }
    
    public class GoalManager : MonoBehaviour
    {
        private NpcWorldData npcWorldData;
        private NpcHandler npcHandler;
        private GoapAgent goapAgent;
        private Goal highestPriorityGoal;
        [SerializeField] public Debugging debuggingPanel;
        private List<Goal> goals = new ();
        [SerializeField]
        private List<GoalInitializer> goalDefinitions = new List<GoalInitializer>();
        private void Start()
        {
            
            debuggingPanel = GetComponent<Debugging>();
            npcWorldData = GetComponent<NpcWorldData>();
            npcHandler = GetComponent<NpcHandler>();
            goapAgent = GetComponent<GoapAgent>();
            InitializeGoals();
        }
        public void InitializeGoals()
        {
            foreach (var definition in goalDefinitions)
            {
                Attribute relatedAttribute = null;
                if (definition.goalDefinition.key == "socialize") relatedAttribute = npcHandler.Social;
                if (definition.goalDefinition.key  == "avoidStarving") relatedAttribute = npcHandler.Hunger;
                if (definition.goalDefinition.key == "seekSafety") relatedAttribute = npcHandler.Hp;
                if(definition.goalDefinition.key == "regenerateStamina") relatedAttribute = npcHandler.Stamina;
                if (definition.goalDefinition.key  == "beAltruistic") relatedAttribute = npcHandler.Altruism;
                if (definition.goalDefinition.key == "work") relatedAttribute = npcHandler.Work;
                if (definition.goalDefinition.key == "improveCognitive") relatedAttribute = npcHandler.IntellectualCuriosity;
                if(definition.goalDefinition.key == "earnMoney") relatedAttribute = npcHandler.PersonalWealth;
                //if(definition.goalDefinition.key == "selfActualization") relatedAttribute = npcHandler.PersonalGrowth;
                var goal = new Goal(definition.goalDefinition.key , definition.goalDefinition.value, definition.goalDefinition.isRemovable, definition.goalDefinition.priority, relatedAttribute);
             
                goals.Add(goal);
            }

        }
        private void EvaluateGoalPriorities()
        {
            // Assigns the priority to the goals 

            float A = 100f;
            float B = 0.05f; // determines how fast the curve decreases, the larger the value the faster the decrease
            float C = 0.08f;
            foreach (var goal in goals)
            {
                if (goal.RelatedAttribute != null)
                {
                    float currentValue = goal.RelatedAttribute.CurrentValue;
                    bool isDire = goal.GoalState.Key == "avoidStarving" || goal.GoalState.Key == "regenerateStamina" || goal.GoalState.Key == "seekSafety";
                    float priority = 0;
                    if (isDire)
                    {
                        priority = A * Mathf.Exp(-B * currentValue);
                    }
                    else
                    {
                        priority = A * Mathf.Exp(-C * currentValue);
                    }
                    goal.Priority = priority;
                }  
   
            }
        }
        public HashSet<KeyValuePair<string, object>> CreateGoalState()
        {
            HashSet<KeyValuePair<string,object>> goal = new HashSet<KeyValuePair<string,object>> ();

            EvaluateGoalPriorities();
            if (goals.Count > 0)
            {
                highestPriorityGoal = goals.OrderByDescending(g => g.Priority).First();
                debuggingPanel.ChangeDebugText("currentGoalPrio", $"<color=purple>Current Goal</color> {highestPriorityGoal.GoalState.Key} {highestPriorityGoal.GoalState.Value} Prio: {highestPriorityGoal.Priority}");
                goapAgent.LoadActions(highestPriorityGoal.GoalState.Key);
                goal.Add(highestPriorityGoal.GoalState);
                
            }
            else
            {
                Debug.LogError("No goals found");
            }

            return goal;
        }
        
        public void RemoveCurrentGoal()
        {
            // Retrieve current goal
            Goal currentGoal = GetCurrentGoal();
            if (currentGoal == null) return;
            // Remove current goal from goals
            if (currentGoal.IsRemovable)
            {
                goals.Remove(currentGoal);
            }
            else
            {
                AdjustNonRemovableGoal(currentGoal);
            }

        }

        private static void AdjustNonRemovableGoal(Goal currentGoal)
        {
            currentGoal.Priority = 0f;
            if (currentGoal.RelatedAttribute != null)
            {
                // todo we probably want this to be handled in action itself
                currentGoal.RelatedAttribute.CurrentValue = 100f;
            }
        }

        public Goal GetCurrentGoal()
        {
            // Whatever the NPC is doing right now 
            return highestPriorityGoal; 
        }
    }
}