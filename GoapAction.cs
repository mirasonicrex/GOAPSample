using System;
using System.Collections.Generic;
using NPCS.Schedule.Goap;
using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCS.Schedule
{
 public abstract class GoapAction : MonoBehaviour {
    private HashSet<KeyValuePair<string,object>> preconditions;
    private HashSet<KeyValuePair<string,object>> effects;
    public KeyValuePair<string, object> Goal { get; set; }
    private bool inRange = false;
    
    protected float StartTime = 0f;
    private float TravelCost { get; } = 1f;


    /* The cost of performing the action. 
* Changing it will affect what actions are chosen during planning.*/
    [field: SerializeField] public float BaseCost { get; private set; } = 1f;
    [field: SerializeField] public float Duration { get; private set; } = 3f;
    
    [field: SerializeField ,  InlineEditor()] private ActionState[] actionPreconditions;
    [field: SerializeField, InlineEditor()] private ActionState[] actionEffects;
    
    [field: SerializeField] 
    protected bool insideBuilding = false; 
    /** 
* An object that the action must be performed on. Can be null. */
    public GameObject Target { get; protected set; }

    protected NpcHandler NpcHandler;
    protected NpcAnimationHandler NpcAnimation;
    protected GameObject Agent; // This is the agent using the action
    private void Awake() {
        preconditions = new HashSet<KeyValuePair<string, object>> ();
        effects = new HashSet<KeyValuePair<string, object>> ();
        foreach (ActionState precondition in actionPreconditions) {
            preconditions.Add(new KeyValuePair<string, object>(precondition.Name, precondition.Value));
        }

        foreach (ActionState effect in actionEffects) {
            effects.Add(new KeyValuePair<string, object>(effect.Name, effect.Value));
        }
        
    }
  
    public void DoReset() {
        inRange = false;
        Target = null;
   ;    StartTime = 0; 
        Reset ();
    }

    /** 
* Reset any variables that need to be reset before planning happens again. 
*/
    public abstract void Reset();
    /** 
* Is the action done? 
*/
    public abstract bool IsDone();
    /** 
* Procedurally check if this action can run. Not all actions 
* will need this, but some might. 
*/
    public abstract bool CheckProceduralPrecondition(GameObject agent);
    
    /** 
* Run the action. 
* Returns True if the action performed successfully or false 
* if something happened and it can no longer perform. In this case 
* the action queue should clear out and the goal cannot be reached. 
*/
    public abstract bool Perform(GameObject agent);
    
    
    public void SetAgent(GameObject agent) {
        Agent = agent;
        NpcHandler = agent.GetComponent<NpcHandler>();
        NpcAnimation = agent.GetComponent<NpcAnimationHandler>();
        if (NpcHandler == null) {
            Debug.LogError("NpcHandler component missing on " + agent.name);
        }
        if (NpcAnimation == null) {
            Debug.LogError("NpcAnimationHandler component missing on " + agent.name);
        }
    }

    public virtual void SetStartTime()
    {
        if (StartTime == 0) StartTime = Time.time;
    }

    public virtual bool CheckDuration()
    {
        return Time.time - StartTime > Duration;
    }
    /** 
* Does this action need to be within range of a target game object? 
* If not then the moveTo state will not need to run for this action. 
*/
    public abstract bool RequiresInRange ();
    
    /** 
* Are we in range of the target? 
* The MoveTo state will set this and it gets reset each time this action is performed. 
*/
    public bool IsInRange () => inRange;

    public void SetInRange(bool inRange) {
        this.inRange = inRange;
    }
    public void AddPrecondition(string key, object value) {
        preconditions.Add (new KeyValuePair<string, object>(key, value) );
    }
    public void RemovePrecondition(string key) {
        KeyValuePair<string, object> remove = default(KeyValuePair<string,object>);
        foreach (KeyValuePair<string, object> kvp in preconditions) {
            if (kvp.Key.Equals (key))
                remove = kvp;
        }
        if ( !default(KeyValuePair<string,object>).Equals(remove) )
            preconditions.Remove (remove);
    }
    public void AddEffect(string key, object value) {
        effects.Add (new KeyValuePair<string, object>(key, value) );
    }
    public void RemoveEffect(string key) {
        KeyValuePair<string, object> remove = default(KeyValuePair<string,object>);
        foreach (KeyValuePair<string, object> kvp in effects) {
            if (kvp.Key.Equals (key))
                remove = kvp;
        }
        if ( !default(KeyValuePair<string,object>).Equals(remove) )
            effects.Remove (remove);
    }
    
    public HashSet<KeyValuePair<string, object>> Preconditions => preconditions;
    public HashSet<KeyValuePair<string, object>> Effects => effects;

    
    private float EstimateTravelTime()
    {
        if (Target == null || insideBuilding) // Don't take into account distance if inside a building
            return 0;

        float distance = Vector2.Distance(transform.position, Target.transform.position);
        float time = distance / 0.5f; 
        return time;
    }

    private float CalculateTravelCost()
    {
        if (insideBuilding) // No travel cost if inside a building
            return 0;

        float time = EstimateTravelTime();
        return  time * TravelCost;
    }
    private float CalculateCost()
    {
        // TODO add more factors for determining cost of node
        throw new InvalidOperationException();
    }

    public float GetCost()
    {
        float cost = BaseCost + CalculateTravelCost();
        return cost;
    }

    public GameObject FindClosestWithTag(string tag, Vector3 position)
    {
        GameObject[] taggedObjects = GameObject.FindGameObjectsWithTag(tag);
        GameObject closest = null;
        float closestDistance = Mathf.Infinity;

        foreach (GameObject obj in taggedObjects)
        {
            float distance = Vector3.Distance(obj.transform.position, position);
            if (distance < closestDistance)
            {
                closest = obj;
                closestDistance = distance;
            }
        }

        return closest;
    }

    
}
}