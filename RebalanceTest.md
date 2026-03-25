# Manual Test: Kafka Consumer Rebalance Survival

Phase 1 exit criterion: "Consumer survives Kafka rebalance without data loss."

This test cannot be reliably automated because it requires triggering a Kafka consumer group rebalance mid-stream and verifying no messages were lost. Run it manually against the docker-compose environment.

## Prerequisites

```bash
docker compose up -d
# Wait for all services to be healthy
docker compose ps
```

Install the `kcat` (formerly `kafkacat`) CLI tool for producing/consuming:

```bash
# Ubuntu/Debian
sudo apt install kafkacat
# macOS
brew install kcat
```

## Test Procedure

### 1. Produce a known set of events

Generate 1,000 numbered events so you can detect gaps:

```bash
for i in $(seq 1 1000); do
  echo "{\"eventId\":\"$(uuidgen)\",\"timestamp\":\"$(date -u +%FT%T.%3NZ)\",\"sessionId\":\"rebalance-test\",\"traceId\":\"trace-$i\",\"agentId\":\"rebalance-agent\",\"agentName\":\"RebalanceAgent\",\"eventType\":\"tool_invocation\",\"toolName\":\"tool-$i\"}" | \
  kcat -P -b localhost:9092 -t agent-events
done
```

### 2. Verify baseline: all events ingested

Wait a few seconds for the consumer to process, then count rows:

```bash
docker compose exec timescaledb psql -U siem -d agentsiem -c \
  "SELECT COUNT(*) FROM agent_events WHERE session_id = 'rebalance-test';"
```

Expected: `1000`

### 3. Trigger a rebalance

Start a second temporary consumer in the same group. This forces Kafka to rebalance partitions between the two consumers. We must use `kafka-console-consumer.sh` from inside the Kafka container (not kcat) because the siem-api consumer uses the `CooperativeSticky` assignment strategy — all group members must use the same protocol:

```bash
docker compose exec kafka /opt/kafka/bin/kafka-console-consumer.sh \
  --bootstrap-server localhost:9092 \
  --topic agent-events \
  --group siem-processors \
  --consumer-property partition.assignment.strategy=org.apache.kafka.clients.consumer.CooperativeStickyAssignor \
  --timeout-ms 5000
```

This joins the `siem-processors` consumer group (triggering a rebalance), reads for 5 seconds, then exits on timeout (triggering another rebalance on leave).

Watch the siem-api logs for rebalance events:

```bash
docker compose logs -f siem-api 2>&1 | grep -i "partition"
```

You should see log lines like:
```
Partitions revoked: 0
Partitions assigned: 0
```

### 4. Produce more events during/after rebalance

While the rebalance is in progress (or immediately after), produce another batch:

```bash
for i in $(seq 1001 2000); do
  echo "{\"eventId\":\"$(uuidgen)\",\"timestamp\":\"$(date -u +%FT%T.%3NZ)\",\"sessionId\":\"rebalance-test-2\",\"traceId\":\"trace-$i\",\"agentId\":\"rebalance-agent\",\"agentName\":\"RebalanceAgent\",\"eventType\":\"tool_invocation\",\"toolName\":\"tool-$i\"}" | \
  kcat -P -b localhost:9092 -t agent-events
done
```

### 5. Verify no data loss

Wait a few seconds, then count both batches:

```bash
docker compose exec timescaledb psql -U siem -d agentsiem -c \
  "SELECT session_id, COUNT(*) FROM agent_events WHERE session_id IN ('rebalance-test', 'rebalance-test-2') GROUP BY session_id;"
```

Expected:
```
    session_id     | count
-------------------+-------
 rebalance-test    |  1000
 rebalance-test-2  |  1000
```

### 6. Verify no duplicates

The at-least-once delivery model means duplicates are possible but should be rare under normal rebalance. Check:

```bash
docker compose exec timescaledb psql -U siem -d agentsiem -c \
  "SELECT COUNT(*) AS total, COUNT(DISTINCT trace_id) AS distinct_traces
   FROM agent_events
   WHERE session_id IN ('rebalance-test', 'rebalance-test-2');"
```

- `total` should equal `distinct_traces` (no duplicates) or be very close
- A small number of duplicates (< 5) is acceptable under at-least-once semantics

## Pass Criteria

- All 2,000 events appear in TimescaleDB (zero data loss)
- Duplicates are either zero or minimal (< 0.5%)
- The consumer recovers automatically after rebalance (no manual restart needed)
- siem-api logs show partition revocation and reassignment without errors

## Troubleshooting

**Consumer stuck after rebalance**: Check `docker compose logs siem-api` for `MaxPollIntervalMs` exceeded errors. The consumer config allows 5 minutes between polls, so this should not happen unless the batch writer is stuck.

**Missing events**: Check the dead-letter topic for events that failed processing:

```bash
kcat -C -b localhost:9092 -t agent-events.dead-letter -o beginning -e
```

**Health check**: Query the consumer health endpoint:

```bash
curl -s http://localhost:5000/health | jq .
```
