namespace Siem.Rules.Core

module FieldResolver =

    /// Resolve a dotted field path to a value from the event.
    /// Handles known fields as direct access, falls back to Properties.
    let resolve (field: string) (evt: AgentEvent) : obj option =
        match field with
        | "eventType"    -> Some (box evt.EventType)
        | "agentId"      -> Some (box evt.AgentId)
        | "agentName"    -> Some (box evt.AgentName)
        | "sessionId"    -> Some (box evt.SessionId)
        | "modelId"      -> evt.ModelId      |> Option.map box
        | "inputTokens"  -> evt.InputTokens  |> Option.map box
        | "outputTokens" -> evt.OutputTokens |> Option.map box
        | "latencyMs"    -> evt.LatencyMs    |> Option.map box
        | "toolName"     -> evt.ToolName     |> Option.map box
        | "toolInput"    -> evt.ToolInput    |> Option.map box
        | "toolOutput"   -> evt.ToolOutput   |> Option.map box
        | "contentHash"  -> evt.ContentHash  |> Option.map box
        | dotted ->
            let key =
                if dotted.StartsWith("properties.") then
                    dotted.Substring("properties.".Length)
                else
                    dotted
            evt.Properties
            |> Map.tryFind key
            |> Option.map box
