namespace Siem.Rules.Core

open System.Text.Json

module Serialization =

    /// Parse a JSON condition tree into the F# Condition type.
    let rec parseCondition (json: JsonElement) : Condition =
        let getRequired (propName: string) : JsonElement =
            match json.TryGetProperty(propName) with
            | true, v -> v
            | false, _ -> failwithf "Missing required property '%s' in condition: %s"
                              propName (json.GetRawText())

        let condType = (getRequired "type").GetString()

        match condType with
        | "field" ->
            let field = (getRequired "field").GetString()
            let opStr = (getRequired "operator").GetString()
            let value = getRequired "value"
            let op =
                match opStr with
                | "Eq"         -> Eq
                | "Neq"        -> Neq
                | "Gt"         -> Gt
                | "Lt"         -> Lt
                | "Gte"        -> Gte
                | "Lte"        -> Lte
                | "Contains"   -> Contains
                | "Regex"      -> Regex
                | "StartsWith" -> StartsWith
                | "EndsWith"   -> EndsWith
                | unknown      -> failwithf "Unknown operator: %s" unknown
            Field (field, op, value)

        | "threshold" ->
            let field = (getRequired "field").GetString()
            let limit = (getRequired "limit").GetDouble()
            let above =
                match json.TryGetProperty("above") with
                | true, v -> v.GetBoolean()
                | _       -> true
            Threshold (field, limit, above)

        | "list" ->
            let field  = (getRequired "field").GetString()
            let listId = (getRequired "listId").GetGuid()
            let negated =
                match json.TryGetProperty("negated") with
                | true, v -> v.GetBoolean()
                | _       -> false
            InList (field, listId, negated)

        | "exists" ->
            let field = (getRequired "field").GetString()
            Exists field

        | "any_of" ->
            let field = (getRequired "field").GetString()
            let values =
                (getRequired "values").EnumerateArray()
                |> Seq.toList
            AnyOf (field, values)

        | "and" ->
            let children =
                (getRequired "conditions").EnumerateArray()
                |> Seq.map parseCondition
                |> Seq.toList
            And children

        | "or" ->
            let children =
                (getRequired "conditions").EnumerateArray()
                |> Seq.map parseCondition
                |> Seq.toList
            Or children

        | "not" ->
            let inner = getRequired "inner"
            Not (parseCondition inner)

        | unknown ->
            failwithf "Unknown condition type: %s" unknown
