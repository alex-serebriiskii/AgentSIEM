# When Appropriate

Items deferred from early phases to be addressed when the project matures.

## Infrastructure-as-Code

| Component | Notes |
|-----------|-------|
| Kubernetes manifests | Deployments, services, ingress for siem-api, TimescaleDB, Redis, Kafka |
| Terraform / Pulumi | Cloud infrastructure provisioning (managed Kafka, RDS/TimescaleDB Cloud, ElastiCache) |
| Helm charts | Parameterized deployment for different environments |

**When to revisit**: Once the application is stable and there is a target deployment environment (cloud provider, cluster) identified.
