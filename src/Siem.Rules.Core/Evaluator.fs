namespace Siem.Rules.Core

open System

module Evaluator =

    open Compiler

    /// Abstraction over Redis state operations.
    /// Implemented in C# and passed in as a dependency.
    type IStateProvider =
        abstract IncrementSlidingWindowAsync:
            key: string * window: TimeSpan -> Async<int64>
        abstract GetSequenceProgressAsync:
            key: string -> Async<int>
        abstract SetSequenceProgressAsync:
            key: string * step: int * ttl: TimeSpan -> Async<unit>
        abstract ClearSequenceAsync:
            key: string -> Async<unit>

    type EvaluationResult = {
        Triggered: bool
        RuleId:    Guid
        Severity:  Severity
        Detail:    string option
        Context:   Map<string, obj>
        Actions:   RuleAction list
    }

    let private noMatch ruleId severity =
        { Triggered = false; RuleId = ruleId; Severity = severity;
          Detail = None; Context = Map.empty; Actions = [] }

    let private evaluateSingleEvent (rule: CompiledRule) (evt: AgentEvent) =
        if rule.Predicate evt then
            {
                Triggered = true
                RuleId    = rule.RuleId
                Severity  = rule.Severity
                Detail    = Some "Event matched rule conditions"
                Context   = Map.empty
                Actions   = rule.Actions
            }
        else
            noMatch rule.RuleId rule.Severity

    let private evaluateTemporal
        (state: IStateProvider)
        (rule: CompiledRule)
        (config: TemporalConfig)
        (evt: AgentEvent)
        =
        async {
            if not (rule.Predicate evt) then
                return noMatch rule.RuleId rule.Severity
            else
                let partitionVal =
                    FieldResolver.resolve config.PartitionField evt
                    |> Option.map string
                    |> Option.defaultValue "global"

                let windowKey =
                    sprintf "rule:%O:window:%s" rule.RuleId partitionVal

                let! count =
                    state.IncrementSlidingWindowAsync(
                        windowKey, config.WindowDuration)

                let triggered =
                    match config.Aggregation with
                    | Count -> float count >= config.Threshold
                    | Rate  -> float count / config.WindowDuration.TotalMinutes
                               >= config.Threshold

                if triggered then
                    return {
                        Triggered = true
                        RuleId    = rule.RuleId
                        Severity  = rule.Severity
                        Detail    = Some (sprintf "%d events in %O" (int count) config.WindowDuration)
                        Context   = Map.ofList [ "window_count", box count ]
                        Actions   = rule.Actions
                    }
                else
                    return { noMatch rule.RuleId rule.Severity with
                                Context = Map.ofList [ "window_count", box count ] }
        }

    let private evaluateSequence
        (state: IStateProvider)
        (rule: CompiledRule)
        (config: SequenceConfig)
        (evt: AgentEvent)
        =
        async {
            match rule.CompiledSteps with
            | None ->
                return noMatch rule.RuleId rule.Severity
            | Some steps ->
                let sessionKey =
                    sprintf "rule:%O:seq:%s" rule.RuleId evt.SessionId

                let! currentStep = state.GetSequenceProgressAsync(sessionKey)

                if currentStep >= steps.Length then
                    return noMatch rule.RuleId rule.Severity
                else
                    let (stepLabel, stepPredicate) = steps.[currentStep]

                    if not (stepPredicate evt) then
                        return noMatch rule.RuleId rule.Severity
                    else
                        let nextStep = currentStep + 1
                        let completed = nextStep >= steps.Length

                        if completed then
                            do! state.ClearSequenceAsync(sessionKey)
                            return {
                                Triggered = true
                                RuleId    = rule.RuleId
                                Severity  = rule.Severity
                                Detail    = Some (sprintf
                                    "Sequence complete (%d steps, final: %s)"
                                    steps.Length stepLabel)
                                Context   = Map.ofList [
                                    "sequence_steps", box steps.Length
                                    "final_step", box stepLabel
                                ]
                                Actions   = rule.Actions
                            }
                        else
                            do! state.SetSequenceProgressAsync(
                                    sessionKey, nextStep, config.MaxSpan)
                            return { noMatch rule.RuleId rule.Severity with
                                        Context = Map.ofList [
                                            "sequence_progress", box nextStep
                                            "next_step", box (fst steps.[nextStep])
                                        ] }
        }

    /// Evaluate a single event against a compiled rule.
    let evaluate
        (state: IStateProvider)
        (rule: CompiledRule)
        (evt: AgentEvent)
        : Async<EvaluationResult> =

        async {
            match rule.EvaluationType with
            | SingleEvent ->
                return evaluateSingleEvent rule evt
            | Temporal config ->
                return! evaluateTemporal state rule config evt
            | Sequence config ->
                return! evaluateSequence state rule config evt
        }
