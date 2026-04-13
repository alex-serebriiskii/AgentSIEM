import { get, post, put, del } from "./client";
import type {
  AlertResponse,
  AgentRiskSummaryResponse,
  AlertDistributionResult,
  CreateListRequest,
  CreateRuleRequest,
  CreateSuppressionRequest,
  EngineStatusResponse,
  EventResponse,
  EventVolumeResult,
  ManagedListDetailResponse,
  ManagedListSummaryResponse,
  PaginatedResult,
  RecompileResponse,
  ResolveAlertRequest,
  RuleResponse,
  SessionResponse,
  SessionTimelineResponse,
  SuppressionResponse,
  ToolUsageResult,
  TopAgentResult,
  UpdateListMembersRequest,
  UpdateRuleRequest,
} from "./types";

// ---------------------------------------------------------------------------
// Dashboard
// ---------------------------------------------------------------------------

export function fetchTopAgents(
  hours?: number,
  limit?: number,
  signal?: AbortSignal,
): Promise<TopAgentResult[]> {
  return get("/api/dashboard/top-agents", { params: { hours, limit }, signal });
}

export function fetchEventVolume(
  hours?: number,
  signal?: AbortSignal,
): Promise<EventVolumeResult[]> {
  return get("/api/dashboard/event-volume", { params: { hours }, signal });
}

export function fetchAlertDistribution(
  hours?: number,
  signal?: AbortSignal,
): Promise<AlertDistributionResult[]> {
  return get("/api/dashboard/alert-distribution", {
    params: { hours },
    signal,
  });
}

export function fetchToolUsage(
  hours?: number,
  limit?: number,
  signal?: AbortSignal,
): Promise<ToolUsageResult[]> {
  return get("/api/dashboard/tool-usage", { params: { hours, limit }, signal });
}

// ---------------------------------------------------------------------------
// Alerts
// ---------------------------------------------------------------------------

export interface AlertListParams {
  status?: string;
  severity?: string;
  agent_id?: string;
  page?: number;
  pageSize?: number;
}

export function fetchAlerts(
  params?: AlertListParams,
  signal?: AbortSignal,
): Promise<PaginatedResult<AlertResponse>> {
  return get("/api/alerts", { params: params as Record<string, string | number | boolean | null | undefined>, signal });
}

export function fetchAlert(
  id: string,
  signal?: AbortSignal,
): Promise<AlertResponse> {
  return get(`/api/alerts/${id}`, { signal });
}

export function acknowledgeAlert(
  id: string,
  signal?: AbortSignal,
): Promise<AlertResponse> {
  return put(`/api/alerts/${id}/acknowledge`, undefined, { signal });
}

export function resolveAlert(
  id: string,
  body?: ResolveAlertRequest,
  signal?: AbortSignal,
): Promise<AlertResponse> {
  return put(`/api/alerts/${id}/resolve`, body, { signal });
}

// ---------------------------------------------------------------------------
// Events
// ---------------------------------------------------------------------------

export interface EventSearchParams {
  start?: string;
  end?: string;
  agent_id?: string;
  event_type?: string;
  session_id?: string;
  tool_name?: string;
  properties?: string;
  page?: number;
  pageSize?: number;
}

export function searchEvents(
  params?: EventSearchParams,
  signal?: AbortSignal,
): Promise<PaginatedResult<EventResponse>> {
  return get("/api/events", { params: params as Record<string, string | number | boolean | null | undefined>, signal });
}

// ---------------------------------------------------------------------------
// Sessions
// ---------------------------------------------------------------------------

export interface SessionListParams {
  agent_id?: string;
  has_alerts?: boolean;
}

export function fetchSessions(
  params?: SessionListParams,
  signal?: AbortSignal,
): Promise<SessionResponse[]> {
  return get("/api/sessions", { params: params as Record<string, string | number | boolean | null | undefined>, signal });
}

export function fetchSession(
  id: string,
  signal?: AbortSignal,
): Promise<SessionResponse> {
  return get(`/api/sessions/${id}`, { signal });
}

export function fetchSessionTimeline(
  id: string,
  limit?: number,
  signal?: AbortSignal,
): Promise<SessionTimelineResponse> {
  return get(`/api/sessions/${id}/timeline`, { params: { limit }, signal });
}

// ---------------------------------------------------------------------------
// Agents
// ---------------------------------------------------------------------------

export function fetchAgentRisk(
  id: string,
  lookback?: string,
  signal?: AbortSignal,
): Promise<AgentRiskSummaryResponse> {
  return get(`/api/agents/${id}/risk`, { params: { lookback }, signal });
}

// ---------------------------------------------------------------------------
// Rules
// ---------------------------------------------------------------------------

export function fetchRules(
  enabled?: boolean,
  signal?: AbortSignal,
): Promise<RuleResponse[]> {
  return get("/api/rules", { params: { enabled }, signal });
}

export function fetchRule(
  id: string,
  signal?: AbortSignal,
): Promise<RuleResponse> {
  return get(`/api/rules/${id}`, { signal });
}

export function createRule(
  body: CreateRuleRequest,
  signal?: AbortSignal,
): Promise<RuleResponse> {
  return post("/api/rules", body, { signal });
}

export function updateRule(
  id: string,
  body: UpdateRuleRequest,
  signal?: AbortSignal,
): Promise<RuleResponse> {
  return put(`/api/rules/${id}`, body, { signal });
}

export function deleteRule(
  id: string,
  signal?: AbortSignal,
): Promise<void> {
  return del(`/api/rules/${id}`, { signal });
}

export function activateRule(
  id: string,
  signal?: AbortSignal,
): Promise<RuleResponse> {
  return post(`/api/rules/${id}/activate`, undefined, { signal });
}

// ---------------------------------------------------------------------------
// Managed Lists
// ---------------------------------------------------------------------------

export function fetchLists(
  signal?: AbortSignal,
): Promise<ManagedListSummaryResponse[]> {
  return get("/api/lists", { signal });
}

export function fetchList(
  id: string,
  signal?: AbortSignal,
): Promise<ManagedListDetailResponse> {
  return get(`/api/lists/${id}`, { signal });
}

export function createList(
  body: CreateListRequest,
  signal?: AbortSignal,
): Promise<ManagedListDetailResponse> {
  return post("/api/lists", body, { signal });
}

export function updateListMembers(
  id: string,
  body: UpdateListMembersRequest,
  signal?: AbortSignal,
): Promise<ManagedListDetailResponse> {
  return put(`/api/lists/${id}/members`, body, { signal });
}

// ---------------------------------------------------------------------------
// Suppressions
// ---------------------------------------------------------------------------

export interface SuppressionListParams {
  rule_id?: string;
  agent_id?: string;
}

export function fetchSuppressions(
  params?: SuppressionListParams,
  signal?: AbortSignal,
): Promise<SuppressionResponse[]> {
  return get("/api/suppressions", { params: params as Record<string, string | number | boolean | null | undefined>, signal });
}

export function createSuppression(
  body: CreateSuppressionRequest,
  signal?: AbortSignal,
): Promise<SuppressionResponse> {
  return post("/api/suppressions", body, { signal });
}

export function deleteSuppression(
  id: string,
  signal?: AbortSignal,
): Promise<void> {
  return del(`/api/suppressions/${id}`, { signal });
}

// ---------------------------------------------------------------------------
// Engine
// ---------------------------------------------------------------------------

export function fetchEngineStatus(
  signal?: AbortSignal,
): Promise<EngineStatusResponse> {
  return get("/api/engine/status", { signal });
}

export function recompileEngine(
  signal?: AbortSignal,
): Promise<RecompileResponse> {
  return post("/api/engine/recompile", undefined, { signal });
}
