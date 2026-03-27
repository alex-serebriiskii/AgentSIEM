namespace Siem.Rules.Core

module Engine =

    open Compiler
    open Evaluator

    /// The engine holds compiled rules and evaluates events against all of them.
    type RuleEngine = {
        CompiledRules: CompiledRule list
        State:         IStateProvider
    }

    /// Evaluate a single event against all active rules.
    /// Returns only the results that triggered.
    let evaluateEvent (engine: RuleEngine) (evt: AgentEvent) : Async<EvaluationResult list> =
        async {
            let! results =
                engine.CompiledRules
                |> List.map (fun rule ->
                    async {
                        let! outcome = Async.Catch(evaluate engine.State rule evt)
                        match outcome with
                        | Choice1Of2 result -> return Some result
                        | Choice2Of2 _      -> return None
                    })
                |> Async.Parallel

            return
                results
                |> Array.choose (fun opt ->
                    match opt with
                    | Some r when r.Triggered -> Some r
                    | _ -> None)
                |> Array.toList
        }

    /// Compile all enabled rules from definitions.
    let compileAllRules
        (listResolver: Compiler.ListResolver)
        (rules: RuleDefinition list)
        : CompiledRule list =

        rules
        |> List.filter (fun r -> r.Enabled)
        |> List.map (compileRule listResolver)
