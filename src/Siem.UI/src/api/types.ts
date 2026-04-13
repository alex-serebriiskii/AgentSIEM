// ---------------------------------------------------------------------------
// Enums (string unions — ergonomic with JSON, exhaustive via TS)
// ---------------------------------------------------------------------------

export type Severity = "low" | "medium" | "high" | "critical";

export type AlertStatus = "open" | "acknowledged" | "resolved";

export type EvaluationType = "SingleEvent" | "Temporal" | "Sequence";

// ---------------------------------------------------------------------------
// Response types — camelCase matching ASP.NET System.Text.Json defaults
// ---------------------------------------------------------------------------

export interface AlertResponse {
  alertId: string;
  ruleId: string;
  ruleName: string;
  severity: Severity;
  status: AlertStatus;
  title: string;
  detail: string | null;
  context: Record<string, unknown>;
  agentId: string;
  sessionId: string | null;
  triggeredAt: string;
  acknowledgedAt: string | null;
  resolvedAt: string | null;
  assignedTo: string | null;
  resolutionNote: string | null;
  labels: Record<string, unknown>;
  suppressed: boolean;
  suppressedBy: string | null;
  suppressionExpiresAt: string | null;
  alertEvents: AlertEventResponse[] | null;
}

export interface AlertEventResponse {
  eventId: string;
  eventTimestamp: string;
  sequenceOrder: number | null;
}

export interface EventResponse {
  eventId: string;
  timestamp: string;
  agentId: string;
  agentName: string;
  eventType: string;
  sessionId: string | null;
  toolName: string | null;
  modelId: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  latencyMs: number | null;
  properties: Record<string, unknown>;
  sourceSdk: string | null;
}

export interface SessionResponse {
  sessionId: string;
  agentId: string;
  agentName: string;
  startedAt: string;
  lastEventAt: string;
  eventCount: number;
  hasAlerts: boolean;
  alertCount: number;
  maxSeverity: Severity | null;
  metadata: Record<string, unknown>;
}

export interface SessionTimelineResponse {
  sessionId: string;
  eventCount: number;
  events: SessionTimelineEventResponse[];
}

export interface SessionTimelineEventResponse {
  eventId: string;
  timestamp: string;
  eventType: string;
  agentId: string;
  toolName: string | null;
  modelId: string | null;
  inputTokens: number | null;
  outputTokens: number | null;
  latencyMs: number | null;
  alertIds: string[];
  alertSeverities: Severity[];
}

export interface RuleResponse {
  id: string;
  name: string;
  description: string;
  enabled: boolean;
  severity: Severity;
  conditionJson: Record<string, unknown>;
  evaluationType: EvaluationType;
  temporalConfig: Record<string, unknown> | null;
  sequenceConfig: Record<string, unknown> | null;
  actionsJson: Record<string, unknown> | null;
  tags: string[];
  createdBy: string;
  createdAt: string;
  updatedAt: string;
}

export interface ManagedListSummaryResponse {
  id: string;
  name: string;
  description: string;
  enabled: boolean;
  memberCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface ManagedListDetailResponse {
  id: string;
  name: string;
  description: string;
  enabled: boolean;
  members: ManagedListMemberResponse[];
  createdAt: string;
  updatedAt: string;
}

export interface ManagedListMemberResponse {
  value: string;
  addedAt: string;
}

export interface SuppressionResponse {
  id: string;
  ruleId: string | null;
  agentId: string | null;
  reason: string;
  createdBy: string;
  createdAt: string;
  expiresAt: string;
  isActive: boolean;
}

export interface AgentRiskSummaryResponse {
  agentId: string;
  agentName: string;
  totalEvents: number;
  totalSessions: number;
  openAlerts: number;
  criticalAlerts: number;
  uniqueTools: number;
  totalTokens: number;
  avgLatencyMs: number;
  eventsPerMinute: number;
  topEventTypes: Record<string, unknown>;
  topTools: Record<string, unknown>;
}

// ---------------------------------------------------------------------------
// Dashboard types
// ---------------------------------------------------------------------------

export interface TopAgentResult {
  agentId: string;
  agentName: string;
  totalEvents: number;
  totalTokens: number;
  maxLatencyMs: number;
}

export interface EventVolumeResult {
  bucket: string;
  eventCount: number;
  totalTokens: number;
}

export interface AlertDistributionResult {
  severity: Severity;
  status: AlertStatus;
  count: number;
}

export interface ToolUsageResult {
  toolName: string;
  invocationCount: number;
  avgLatencyMs: number;
  uniqueSessions: number;
}

// ---------------------------------------------------------------------------
// Engine types
// ---------------------------------------------------------------------------

export interface ListCacheInfo {
  listId: string;
  name: string;
  memberCount: number;
  loadedAt: string;
}

export interface EngineStatusResponse {
  compiledAt: string;
  ruleCount: number;
  listCaches: ListCacheInfo[];
  staleness: string;
}

export interface RecompileResponse {
  status: string;
  compiledAt: string;
  ruleCount: number;
}

// ---------------------------------------------------------------------------
// Generic pagination
// ---------------------------------------------------------------------------

export interface PaginatedResult<T> {
  data: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

// ---------------------------------------------------------------------------
// SignalR payload
// ---------------------------------------------------------------------------

export interface SignalRAlertPayload {
  alertId: string;
  ruleId: string;
  ruleName: string;
  severity: Severity;
  title: string;
  agentId: string;
  agentName: string;
  sessionId: string | null;
  triggeredAt: string;
  recentAlertCount: number;
  labels: Record<string, unknown>;
}

// ---------------------------------------------------------------------------
// Request types
// ---------------------------------------------------------------------------

export interface ResolveAlertRequest {
  resolutionNote?: string | null;
}

export interface CreateRuleRequest {
  name: string;
  description: string;
  severity?: Severity;
  conditionJson: Record<string, unknown>;
  evaluationType?: EvaluationType;
  temporalConfig?: Record<string, unknown> | null;
  sequenceConfig?: Record<string, unknown> | null;
  actionsJson?: Record<string, unknown> | null;
  tags?: string[];
  createdBy: string;
}

export interface UpdateRuleRequest {
  name?: string | null;
  description?: string | null;
  severity?: Severity | null;
  conditionJson?: Record<string, unknown> | null;
  evaluationType?: EvaluationType | null;
  temporalConfig?: Record<string, unknown> | null;
  sequenceConfig?: Record<string, unknown> | null;
  actionsJson?: Record<string, unknown> | null;
  tags?: string[] | null;
  createdBy?: string | null;
}

export interface CreateListRequest {
  name: string;
  description: string;
  enabled?: boolean;
  members?: string[];
}

export interface UpdateListMembersRequest {
  members: string[];
}

export interface CreateSuppressionRequest {
  ruleId?: string | null;
  agentId?: string | null;
  reason: string;
  createdBy: string;
  durationMinutes: number;
}
