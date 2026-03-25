namespace Siem.Rules.Core

open System
open System.Text.Json

/// Comparison operators for field conditions.
type ComparisonOp =
    | Eq | Neq | Gt | Lt | Gte | Lte | Contains | Regex | StartsWith | EndsWith

/// The condition tree. Each node is a variant, not a subclass.
/// The entire tree is immutable by default.
type Condition =
    | Field     of field: string * op: ComparisonOp * value: JsonElement
    | Threshold of field: string * limit: float * above: bool
    | InList    of field: string * listId: Guid * negated: bool
    | And       of Condition list
    | Or        of Condition list
    | Not       of Condition
    | Exists    of field: string
    | AnyOf     of field: string * values: JsonElement list

type TemporalAggregation = Count | Rate

type TemporalConfig = {
    WindowDuration:  TimeSpan
    Threshold:       float
    Aggregation:     TemporalAggregation
    PartitionField:  string
}

type SequenceStep = {
    Label:     string
    Condition: Condition
}

type SequenceConfig = {
    MaxSpan: TimeSpan
    Steps:   SequenceStep list
}

type EvaluationType =
    | SingleEvent
    | Temporal of TemporalConfig
    | Sequence of SequenceConfig

type Severity = Low | Medium | High | Critical

type RuleAction =
    | CreateAlert  of labels: Map<string, string> * assignTo: string option
    | EnrichEvent  of fields: Map<string, string>
    | Suppress     of duration: TimeSpan * reason: string
    | Webhook      of url: string * headers: Map<string, string>

type RuleDefinition = {
    Id:             Guid
    Name:           string
    Description:    string
    Enabled:        bool
    Severity:       Severity
    Condition:      Condition
    EvaluationType: EvaluationType
    Actions:        RuleAction list
    Tags:           string list
    CreatedBy:      string
    CreatedAt:      DateTime
    UpdatedAt:      DateTime
}

/// Simplified event interface that the compiled predicates operate on.
type AgentEvent = {
    EventId:      Guid
    Timestamp:    DateTime
    SessionId:    string
    TraceId:      string
    AgentId:      string
    AgentName:    string
    EventType:    string
    ModelId:      string option
    InputTokens:  int option
    OutputTokens: int option
    LatencyMs:    float option
    ToolName:     string option
    ToolInput:    string option
    ToolOutput:   string option
    ContentHash:  string option
    Properties:   Map<string, JsonElement>
}
