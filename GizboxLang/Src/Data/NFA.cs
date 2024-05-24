using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

using Gizbox;




public class NFA
{
    public class State
    {
        public bool IsAcceptState { get; set; }
        public List<Transition> Transitions { get; private set; } = new List<Transition>();

        public void AddTransition(char? symbol, State state)
        {
            Transitions.Add(new Transition(symbol, state));
        }
    }

    public class Transition
    {
        public char? Symbol { get; } // Nullable char to handle ε-transitions
        public State NextState { get; }

        public Transition(char? symbol, State nextState)
        {
            Symbol = symbol;
            NextState = nextState;
        }
    }








    private State startState;
    private State currentState;
    private int nextStateId = 0;
    private Dictionary<char, List<State>> charClasses = new Dictionary<char, List<State>>();

    public NFA(string pattern)
    {
        startState = new State();
        currentState = startState;
        Parse(pattern);
    }

    private void Parse(string pattern)
    {
        var operators = new Stack<State>();
        var operands = new Stack<State>();

        int i = 0;
        while (i < pattern.Length)
        {
            switch (pattern[i])
            {
                case '|':
                    // Handle alternation
                    operators.Push(currentState);
                    currentState = new State();
                    break;

                case '*':
                case '+':
                case '?':
                    // Handle quantifiers
                    HandleQuantifier(pattern[i]);
                    break;

                case '[':
                    // Handle character class
                    i++;
                    HandleCharClass(pattern, ref i);
                    break;

                default:
                    // Handle literal characters
                    State newState = new State();
                    currentState.AddTransition(pattern[i], newState);
                    currentState = newState;
                    break;
            }
            i++;
        }

        // Finalize NFA construction
        currentState.IsAcceptState = true;
        while (operators.Count > 0)
        {
            State left = operands.Pop();
            State op = operators.Pop();
            State right = operands.Pop();
            // Link states based on operator
            // This part needs customization based on how you handle '|' and other operators
        }
    }

    private void HandleQuantifier(char quantifier)
    {
        Transition lastTransition = currentState.Transitions[currentState.Transitions.Count - 1];
        switch (quantifier)
        {
            case '*':
                // Zero or more
                lastTransition.NextState.AddTransition(lastTransition.Symbol, lastTransition.NextState);
                break;
            case '+':
                // One or more
                lastTransition.NextState.AddTransition(lastTransition.Symbol, currentState);
                break;
            case '?':
                // Zero or one
                currentState.AddTransition(null, lastTransition.NextState); // ε-transition
                break;
        }
    }

    private void HandleCharClass(string pattern, ref int i)
    {
        while (pattern[i] != ']')
        {
            char start = pattern[i++];
            if (pattern[i] == '-' && i + 1 < pattern.Length && pattern[i + 1] != ']')
            {
                // Range, e.g., a-z
                i++;
                char end = pattern[i++];
                for (char c = start; c <= end; c++)
                {
                    currentState.AddTransition(c, new State());
                }
            }
            else
            {
                // Single character in class
                currentState.AddTransition(start, new State());
            }
        }
    }

    public bool Matches(string input)
    {
        var currentStates = new HashSet<State> { startState };
        // Logic to follow transitions based on input
        // ...
        return currentStates.Any(s => s.IsAcceptState);
    }
}

public class NFATest
{
    public static void Test()
    {
        NFA nfa = new NFA("a|b*");
        Debug.LogLine(nfa.Matches("aaaa")); // Should be true
        Debug.LogLine(nfa.Matches("b"));    // Should be true
    }
}
