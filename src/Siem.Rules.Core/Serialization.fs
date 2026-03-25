namespace Siem.Rules.Core

open System.Text.Json

module Serialization =

    /// Parse a JSON condition tree into the F# Condition type.
    let rec parseCondition (json: JsonElement) : Condition =
        let condType = json.GetProperty("type").GetString()

        match condType with
        | "field" ->
            let field = json.GetProperty("field").GetString()
            let opStr = json.GetProperty("operator").GetString()
            let value = json.GetProperty("value")
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
            let field = json.GetProperty("field").GetString()
            let limit = json.GetProperty("limit").GetDouble()
            let above =
                match json.TryGetProperty("above") with
                | true, v -> v.GetBoolean()
                | _       -> true
            Threshold (field, limit, above)

        | "list" ->
            let field  = json.GetProperty("field").GetString()
            let listId = json.GetProperty("listId").GetGuid()
            let negated =
                match json.TryGetProperty("negated") with
                | true, v -> v.GetBoolean()
                | _       -> false
            InList (field, listId, negated)

        | "exists" ->
            let field = json.GetProperty("field").GetString()
            Exists field

        | "any_of" ->
            let field = json.GetProperty("field").GetString()
            let values =
                json.GetProperty("values").EnumerateArray()
                |> Seq.toList
            AnyOf (field, values)

        | "and" ->
            let children =
                json.GetProperty("conditions").EnumerateArray()
                |> Seq.map parseCondition
                |> Seq.toList
            And children

        | "or" ->
            let children =
                json.GetProperty("conditions").EnumerateArray()
                |> Seq.map parseCondition
                |> Seq.toList
            Or children

        | "not" ->
            let inner = json.GetProperty("inner")
            Not (parseCondition inner)

        | unknown ->
            failwithf "Unknown condition type: %s" unknown
