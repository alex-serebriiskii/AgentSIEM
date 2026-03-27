namespace Siem.Rules.Core

open System
open System.Text.Json

module Compiler =

    open FieldResolver

    /// External dependency: resolves a managed list ID to its current members.
    type ListResolver = Guid -> Set<string>

    /// Attempt to convert an arbitrary value to a double, returning None on failure.
    let private tryToDouble (v: obj) : float option =
        match v with
        | :? float as f -> Some f
        | :? int as i -> Some (float i)
        | :? int64 as i -> Some (float i)
        | _ ->
            match Double.TryParse(v.ToString(), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
            | true, d -> Some d
            | false, _ -> None

    /// Compare a resolved field value against a JSON element using the given operator.
    let private compareField (op: ComparisonOp) (fieldVal: obj option) (target: JsonElement) : bool =
        match fieldVal with
        | None -> false
        | Some v ->
            let strVal = v.ToString()
            match op with
            | Eq         -> strVal = (target.ToString())
            | Neq        -> strVal <> (target.ToString())
            | Contains   -> strVal.Contains(target.GetString(), StringComparison.OrdinalIgnoreCase)
            | StartsWith -> strVal.StartsWith(target.GetString(), StringComparison.OrdinalIgnoreCase)
            | EndsWith   -> strVal.EndsWith(target.GetString(), StringComparison.OrdinalIgnoreCase)
            | Regex      -> Text.RegularExpressions.Regex.IsMatch(strVal, target.GetString())
            | Gt  -> match tryToDouble v with Some d -> d >  target.GetDouble() | None -> false
            | Lt  -> match tryToDouble v with Some d -> d <  target.GetDouble() | None -> false
            | Gte -> match tryToDouble v with Some d -> d >= target.GetDouble() | None -> false
            | Lte -> match tryToDouble v with Some d -> d <= target.GetDouble() | None -> false

    /// Compile a condition tree into an executable predicate.
    /// Called once per rule at load time, not per event.
    let rec compile (listResolver: ListResolver) (condition: Condition) : (AgentEvent -> bool) =

        match condition with

        | Field (field, op, value) ->
            fun evt ->
                let fieldVal = resolve field evt
                compareField op fieldVal value

        | Threshold (field, limit, above) ->
            fun evt ->
                match resolve field evt with
                | Some v ->
                    match tryToDouble v with
                    | Some numVal -> if above then numVal > limit else numVal < limit
                    | None -> false
                | None -> false

        | InList (field, listId, negated) ->
            let members = listResolver listId
            fun evt ->
                match resolve field evt with
                | Some v ->
                    let inList = members.Contains(v.ToString())
                    if negated then not inList else inList
                | None -> negated

        | Exists field ->
            fun evt ->
                (resolve field evt).IsSome

        | AnyOf (field, values) ->
            let valueStrings = values |> List.map (fun v -> v.ToString()) |> Set.ofList
            fun evt ->
                match resolve field evt with
                | Some v -> valueStrings.Contains(v.ToString())
                | None -> false

        | And conditions ->
            let compiled = conditions |> List.map (compile listResolver)
            fun evt -> compiled |> List.forall (fun pred -> pred evt)

        | Or conditions ->
            let compiled = conditions |> List.map (compile listResolver)
            fun evt -> compiled |> List.exists (fun pred -> pred evt)

        | Not inner ->
            let compiled = compile listResolver inner
            fun evt -> not (compiled evt)

    /// A compiled rule ready for evaluation.
    type CompiledRule = {
        RuleId:         Guid
        Severity:       Severity
        Predicate:      AgentEvent -> bool
        EvaluationType: EvaluationType
        CompiledSteps:  (string * (AgentEvent -> bool)) list option
        Actions:        RuleAction list
    }

    let compileRule (listResolver: ListResolver) (rule: RuleDefinition) : CompiledRule =
        let predicate = compile listResolver rule.Condition

        let compiledSteps =
            match rule.EvaluationType with
            | Sequence config ->
                config.Steps
                |> List.map (fun step ->
                    step.Label, compile listResolver step.Condition)
                |> Some
            | _ -> None

        {
            RuleId         = rule.Id
            Severity       = rule.Severity
            Predicate      = predicate
            EvaluationType = rule.EvaluationType
            CompiledSteps  = compiledSteps
            Actions        = rule.Actions
        }
