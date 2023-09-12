using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using NPCS.Schedule;
using UnityEngine;
using Debug = UnityEngine.Debug;

/**
 * Plans what actions can be completed in order to fulfill a goal state.
 */
public delegate void OnPlanCompleted(Queue<GoapAction> plan);

public class GoapPlanner
{
    public bool IsPlanning { get; private set; } = false;
    
    private const float maxExecutionTime = 0.1f; // 100 ms, adjust according to your requirements.

    // startTimestamp to record the time when the graph building starts.
    private float startTimestamp;
    private MonoBehaviour monoBehaviour;

    /**
     * Plan what sequence of actions can fulfill the goal.
     * Returns null if a plan could not be found, or a list of the actions
     * that must be performed, in order, to fulfill the goal.
     */
    public IEnumerator Plan(GameObject agent,
        HashSet<GoapAction> availableActions,
        HashSet<KeyValuePair<string, object>> worldState,
        HashSet<KeyValuePair<string, object>> goal, OnPlanCompleted callback)
    {
        IsPlanning = true;
        visitedStates.Clear();
        // reset the actions so we can start fresh with them
        foreach (GoapAction a in availableActions)
        {
            a.DoReset();
        }

        // check what actions can run using their checkProceduralPrecondition
        HashSet<GoapAction> usableActions = new HashSet<GoapAction>();
        foreach (GoapAction a in availableActions)
        {
            if (a.CheckProceduralPrecondition(agent))
            {
                usableActions.Add(a);
                //  Debug.Log($"Found actions for goal {goal}: " + a.GetType().Name);
            }
        }

        // we now have all actions that can run, stored in usableActions
        // build up the tree and record the leaf nodes that provide a solution to the goal.
        List<Node> leaves = new List<Node>();

        // build graph
        Node start = new Node(null, 0, worldState, null);

        yield return buildGraphCoroutine(start, leaves, usableActions, goal, callback);

        IsPlanning = false;
    }

    /**
     * Returns true if at least one solution was found.
     * The possible paths are stored in the leaves list. Each leaf has a
     * 'runningCost' value where the lowest cost will be the best action
     * sequence.
     */
    // Stack for explicit recursion
    private Stack<Node> stack = new Stack<Node>();

    private HashSet<string> visitedStates = new HashSet<string>();

    private IEnumerator buildGraphCoroutine(Node parent, List<Node> leaves, HashSet<GoapAction> usableActions,
        HashSet<KeyValuePair<string, object>> goal, OnPlanCompleted callback)
    {
        // Initialize stack
        stack.Clear();
        stack.Push(parent);
        bool cycleDetected = false;
        bool foundOne = false;
        startTimestamp = Time.realtimeSinceStartup;
        int maxCheckCount = 1000;
        int checkCount = 0;
        Node bestNode = null; // This will store the best (lowest-cost) leaf node found so far.
        float bestCost = float.MaxValue; // Initialize with a very high cost.
        while (stack.Count > 0)
        {
            Node parentNode = stack.Pop();
            foreach (GoapAction action in usableActions)
            {
                if (inState(action.Preconditions, parentNode.state))
                {
                    // create a new state
                    HashSet<KeyValuePair<string, object>>
                        currentState = populateState(parentNode.state, action.Effects);
                    // If we've already visited this state, skip it
                    if (visitedStates.Contains(StateToString(currentState)))
                    {
                        continue;
                    }
                    // Otherwise, mark it as visited
                    visitedStates.Add(StateToString(currentState));
                    if (checkCount++ >= maxCheckCount)
                    {
                        Debug.LogError($"Max check count exceeded in cycle. Planning failed.{goal}");
                        WriteStateToTextFile(parentNode.state, "debugCurrentState.txt");
                        callback?.Invoke(null); // consider adding a specific error callback, if needed

                        yield break; // break coroutine
                    }

                    Node node = new Node(parentNode, parentNode.runningCost + action.GetCost(), currentState, action);

                    WriteNodeToTextFile(node);

                    if (inState(goal, currentState))
                    {
                        if (node.runningCost < bestCost)
                        {
                            bestNode = node;
                            bestCost = node.runningCost;
                        }

                        // Keep track of all leaf nodes.
                        leaves.Add(node);
                    }
                    else
                    {
                        HashSet<GoapAction> subset = actionSubset(usableActions, action);
                        stack.Push(node);
                    }

                    // yield if execution took too long
                    if ((Time.realtimeSinceStartup - startTimestamp) > maxExecutionTime)
                    {
                        yield return null;
                    }
                    // if ((Time.realtimeSinceStartup - startTimestamp) > maxExecutionTime && bestNode != null)
                    // {
                    //     Debug.LogError("Execution taking too long, returning best solution found so far.");
                    //     break;
                    // }
                }
            }

            if (!foundOne)
            {
                WriteStateToTextFile(parentNode.state, "debugCurrentState.txt");
                WriteStateToTextFile(goal, "debugGoalState.txt");
                WriteNodeToTextFile(parentNode);
            }
        }

        if (bestNode != null)
        {
            leaves.Add(bestNode);
        }

        // get its node and work back through the parents
        List<GoapAction> result = new List<GoapAction>(); // todo cache the result 
        Node n = bestNode;
        while (n != null)
        {
            if (n.action != null)
            {
                result.Insert(0, n.action); // insert the action in the front
            }

            n = n.parent;
        }
        // we now have this action list in correct order

        Queue<GoapAction> queue = new Queue<GoapAction>(); //todo cache the result 
        foreach (GoapAction a in result)
        {
            queue.Enqueue(a);
        }

        WritePlanToTextFile(result);

        callback?.Invoke(bestNode != null ? queue : null); // call the callback with the result.
    }

    /**
     * Create a subset of the actions excluding the removeMe one. Creates a new set.
     */
    private HashSet<GoapAction> actionSubset(HashSet<GoapAction> actions, GoapAction removeMe)
    {
        HashSet<GoapAction> subset = new HashSet<GoapAction>();
        foreach (GoapAction a in actions)
        {
            if (!a.Equals(removeMe))
                subset.Add(a);
        }

        return subset;
    }

    /**
     * Check that all items in 'test' are in 'state'. If just one does not match or is not there
     * then this returns false.
     */
    private bool inState(HashSet<KeyValuePair<string, object>> test, HashSet<KeyValuePair<string, object>> state)
    {
        bool allMatch = true;
        foreach (KeyValuePair<string, object> t in test)
        {
            bool match = false;
            foreach (KeyValuePair<string, object> s in state)
            {
                if (s.Equals(t))
                {
                    match = true;
                    break;
                }
            }

            if (!match)
                allMatch = false;
        }

        return allMatch;
    }

    /**
     * Apply the stateChange to the currentState
     */
    private HashSet<KeyValuePair<string, object>> populateState(HashSet<KeyValuePair<string, object>> currentState,
        HashSet<KeyValuePair<string, object>> stateChange)
    {
        // KeyValuePair in C# is a value type and not a reference type. Its trying to replace the old KeyValuePair with a new one,
        // just adding a new entry into the HashSet and not replacing the old one.
        //  now the states should correctly replace the old ones instead of continually adding new ones
        // We clone the current state
        HashSet<KeyValuePair<string, object>> state = new HashSet<KeyValuePair<string, object>>(currentState);

        foreach (KeyValuePair<string, object> change in stateChange)
        {
            bool keyExists = false;

            foreach (KeyValuePair<string, object> kvp in currentState)
            {
                if (kvp.Key.Equals(change.Key))
                {
                    keyExists = true;
                    break;
                }
            }

            if (keyExists)
            {
                state.RemoveWhere(kvp => kvp.Key.Equals(change.Key));
            }

            state.Add(change);
        }

        return state;
    }

    public GoapPlanner(MonoBehaviour monoBehaviour)
    {
        this.monoBehaviour = monoBehaviour;
    }

    /**
     * Used for building up the graph and holding the running costs of actions.
     */
    private class Node
    {
        public Node parent;
        public float runningCost;
        public HashSet<KeyValuePair<string, object>> state;
        public GoapAction action;

        public Node(Node parent, float runningCost, HashSet<KeyValuePair<string, object>> state, GoapAction action)
        {
            this.parent = parent;
            this.runningCost = runningCost;
            this.state = state;
            this.action = action;

            // Debugging output:
            // Debug.Log($"Creating node with action {action} and running cost {runningCost}");
            // if (parent != null)
            // {
            //     Debug.Log($"Parent node has action {parent.action} and running cost {parent.runningCost}");
            // }
            // else
            // {
            //     Debug.Log("This node has no parent");
            // }
            // Write these details to file as well
            WriteNodeToTextFile(this);
        }
    }

    //todo we need debugging for when the plan fails
    private void WritePlanToTextFile(List<GoapAction> actions)
    {
        // Create a StreamWriter to write to a file
        using (StreamWriter writer = new StreamWriter("debugPlan.txt", true)) // true to append data to the file
        {
            foreach (GoapAction action in actions)
            {
                // Write the action and its preconditions and effects to the file
                writer.WriteLine($"Action: {action}");
                writer.WriteLine("Preconditions:");
                foreach (var precondition in action.Preconditions)
                {
                    writer.WriteLine($"    {precondition.Key}: {precondition.Value}");
                }

                writer.WriteLine("Effects: ");
                foreach (var effect in action.Effects)
                {
                    writer.WriteLine($"    {effect.Key}: {effect.Value}");
                }

                writer.WriteLine();
            }
        }
    }

    public static string prettyPrint(HashSet<KeyValuePair<string, object>> state)
    {
        String s = "";
        foreach (KeyValuePair<string, object> kvp in state)
        {
            s += kvp.Key + ":" + kvp.Value.ToString();
            s += ", ";
        }

        return s;
    }

    private void WriteStateToTextFile(HashSet<KeyValuePair<string, object>> state, string fileName)
    {
        // Create a StreamWriter to write to a file
        using (StreamWriter writer = new StreamWriter(fileName, true)) // true to append data to the file
        {
            // Write the state details to the file
            writer.WriteLine("State: ");
            foreach (var stateDetail in state)
            {
                writer.WriteLine($"    {stateDetail.Key}: {stateDetail.Value}");
            }

            writer.WriteLine();
        }
    }

    private static void WriteNodeToTextFile(Node node)
    {
        using (StreamWriter writer = new StreamWriter("debugPlan.txt", true)) // true to append data to the file
        {
            if (node.action != null)
            {
                writer.WriteLine($"Node action: {node.action}");
                writer.WriteLine($"Node running cost: {node.runningCost}");

                writer.WriteLine("Preconditions:");
                foreach (var precondition in node.action.Preconditions)
                {
                    writer.WriteLine($"    {precondition.Key}: {precondition.Value}");
                }

                writer.WriteLine("Effects: ");
                foreach (var effect in node.action.Effects)
                {
                    writer.WriteLine($"    {effect.Key}: {effect.Value}");
                }
            }
            else
            {
                writer.WriteLine("This node has no action");
            }

            writer.WriteLine();
        }
    }

    // method to convert a state to a string
    private string StateToString(HashSet<KeyValuePair<string, object>> state)
    {
        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<string, object> pair in state)
        {
            sb.Append(pair.Key);
            sb.Append(pair.Value.ToString());
        }

        return sb.ToString();
    }

    public static string PrettyPrint(Queue<GoapAction> actions)
    {
        String s = "";
        foreach (GoapAction a in actions)
        {
            s += a.GetType().Name;
            s += "-> ";
        }

        s += "GOAL";
        return s;
    }
}