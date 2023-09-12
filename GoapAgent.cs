using System;
using System.Collections.Generic;
using NPCS.Schedule;
using NPCS.Schedule.Goap;
using PixelCrushers.DialogueSystem;
using UnityEngine;

public class GoapAgent : MonoBehaviour
{
    [SerializeField] private GameObject goalObject;

    private HashSet<GoapAction> availableActions;
    private Queue<GoapAction> currentActions;

    private IGoap
        dataProvider; // this is the implementing class that provides our world data and listens to feedback on planning

    private Dictionary<string, HashSet<GoapAction>> goalActions = new();

    private FSM.FSMState idleState; // finds something to do
    private FSM.FSMState moveToState; // moves to a target
    private FSM.FSMState performActionState; // performs an action

    private GoapPlanner planner;

    //private float cost;
    private ScheduleManager scheduleManager;

    private List<GoapSchedule> schedules = new();
    public FSM StateMachine { get; private set; }

    private void Start()
    {
        StateMachine = new FSM();
        availableActions = new HashSet<GoapAction>();
        currentActions = new Queue<GoapAction>();
        planner = new GoapPlanner(this);
        FindDataProvider();
        CreateIdleState();
        CreateMoveToState();
        CreatePerformActionState();
        StateMachine.pushState(idleState);
    }

    private void Update()
    {
        if (DialogueManager.IsConversationActive && DialogueManager.CurrentConversant.name == gameObject.name) return;
        StateMachine.Update(gameObject);
    }


    public void AddAction(GoapAction a)
    {
        availableActions.Add(a);
    }

    public GoapAction GetAction(Type action)
    {
        foreach (var g in availableActions)
            if (g.GetType() == action)
                return g;

        return null;
    }

    public void RemoveAction(GoapAction action)
    {
        availableActions.Remove(action);
    }

    private bool HasActionPlan()
    {
        return currentActions.Count > 0;
    }


    private void CreateIdleState()
    {
        idleState = (fsm, gameObj) =>
        {
            if (!planner.IsPlanning)
            {
                var worldState = dataProvider.GetWorldState();
                var goal = dataProvider.CreateGoalState();
                // GOAP planning

                // get the world state and the goal we want to plan for

                // Plan
                void PlanCompletedCallback(Queue<GoapAction> plan)
                {
                    if (plan != null && plan.Count > 0)
                    {
                        // we have a plan, hooray!
                        currentActions = plan;
                        dataProvider.PlanFound(goal, plan);
                        fsm.popState(); // move to PerformAction state
                        fsm.pushState(performActionState);
                        // Debug.Log("Plan found");
                    }
                    else
                    {
                        // ugh, we couldn't get a plan
                        Debug.Log("<color=orange>Failed Plan:</color>" + prettyPrint(goal));
                        dataProvider.PlanFailed(goal);
                        fsm.popState(); // move back to IdleAction state

                        fsm.pushState(idleState);
                    }
                }

                // Start the coroutine with the callback.
                StartCoroutine(planner.Plan(gameObject, availableActions, worldState, goal, PlanCompletedCallback));
            }
        };
    }

    private void CreateMoveToState()
    {
        moveToState = (fsm, gameObj) =>
        {
            // move the game object

            var action = currentActions.Peek();
            if (action.RequiresInRange() && action.Target == null)
            {
                Debug.Log(
                    "<color=red>Fatal error:</color> Action requires a target but has none. Planning failed. You did not assign the target in your Action.checkProceduralPrecondition()");
                fsm.popState(); // move
                fsm.popState(); // perform
                fsm.pushState(idleState);
                return;
            }

            // get the agent to move itself
            if (dataProvider.MoveAgent(action)) fsm.popState();
        };
    }


    private void CreatePerformActionState()
    {
        performActionState = (fsm, gameObj) =>
        {
            // perform the action

            if (!HasActionPlan())
            {
                // no actions to perform
                //CompleteCurrentGoal(); 
                Debug.Log("<color=red>Done actions</color>");
                fsm.popState();
                fsm.pushState(idleState);
                dataProvider.ActionsFinished();
                return;
            }

            var action = currentActions.Peek();
            if (action.IsDone())
                // the action is done. Remove it so we can perform the next one
                currentActions.Dequeue();
            //availableActions.Clear();
            //isActionInProgress = false;
            if (HasActionPlan())
            {
                // perform the next action
                action = currentActions.Peek();
                // Debug.Log($"Current Action {action}"); todo add to debugger
                var inRange = !action.RequiresInRange() || action.IsInRange();

                if (inRange)
                {
                    // if (!isActionInProgress)
                    // {
                    // we are in range, so perform the action
                    var success = action.Perform(gameObj);
                    // mark that we're currently performing an action
                    //isActionInProgress = true;

                    if (!success)
                        // action failed, we need to plan again
                        //isActionInProgress = false;
                        dataProvider.PlanAborted(action);
                    // }
                }
                else
                {
                    // we need to move there first
                    // push moveTo state
                    fsm.pushState(moveToState);
                }
            }
            else
            {
                // no actions left, move to Plan state
                // CompleteCurrentGoal(); // remove the current goal 
                availableActions.Clear();
                fsm.popState();
                fsm.pushState(idleState);

                dataProvider.ActionsFinished();
            }
        };
    }

    public void Replan()
    {
        StateMachine.popState();
        StateMachine.pushState(idleState);
    }


    private void FindDataProvider()
    {
        foreach (var comp in gameObject.GetComponents(typeof(Component)))
            if (typeof(IGoap).IsAssignableFrom(comp.GetType()))
            {
                dataProvider = (IGoap)comp;
                return;
            }
    }

    public void LoadActions(string currentGoal)
    {
        if (currentGoal != null)
        {
            // Debug.Log($"Goal name {currentGoal}");

            var goalActionsChild = goalObject.transform.Find(currentGoal);
            // Debug.Log(goalActionsChild);
            if (goalActionsChild != null)
            {
                var actions = goalActionsChild.GetComponents<GoapAction>();
                foreach (var a in actions) availableActions.Add(a);

                Debug.Log($"Found actions for goal {currentGoal}: " + prettyPrint(actions));
            }
            else
            {
                //dataProvider.PlanFailed();
                Debug.LogError($"Child object '{currentGoal}' not found");
            }
        }
        //Debug.Log("Found actions: "+prettyPrint(actions));
    }

    public static string prettyPrint(HashSet<KeyValuePair<string, object>> state)
    {
        var s = "";
        foreach (var kvp in state)
        {
            s += kvp.Key + ":" + kvp.Value;
            s += ", ";
        }

        return s;
    }

    public static string PrettyPrint(Queue<GoapAction> actions)
    {
        var s = "";
        foreach (var a in actions)
        {
            s += a.GetType().Name;
            s += "-> ";
        }

        s += "GOAL";
        return s;
    }

    public static string prettyPrint(GoapAction[] actions)
    {
        var s = "";
        foreach (var a in actions)
        {
            s += a.GetType().Name;
            s += ", ";
        }

        return s;
    }

    public static string prettyPrint(GoapAction action)
    {
        var s = "" + action.GetType().Name;
        return s;
    }
}