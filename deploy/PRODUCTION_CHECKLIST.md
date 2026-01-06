# Production Deployment Checklist

Comprehensive checklist for deploying Memory Indexer MCP Server to production environments.

## Pre-Deployment

### Infrastructure

- [ ] Kubernetes cluster provisioned (v1.24+)
- [ ] kubectl configured with appropriate credentials
- [ ] Persistent storage provisioner configured
- [ ] Ingress controller installed (if exposing externally)
- [ ] Metrics server installed for HPA
- [ ] Monitoring stack deployed (Prometheus/Grafana)

### Dependencies

- [ ] Vector database deployed and accessible
  - [ ] Qdrant cluster running (recommended: v1.7+)
  - [ ] Or SQLite-vec for smaller deployments
  - [ ] Connection string validated
  - [ ] Authentication configured

- [ ] Embedding provider configured
  - [ ] Ollama deployed with bge-m3 model
  - [ ] Or OpenAI API key obtained and tested
  - [ ] Or local ONNX runtime configured
  - [ ] Embedding dimensions verified (1024 for bge-m3)

### Configuration

- [ ] Environment-specific ConfigMap created
  - [ ] Storage connection string updated
  - [ ] Embedding provider configured
  - [ ] VCM settings tuned for workload
  - [ ] Health check thresholds set
  - [ ] Logging levels appropriate for environment

- [ ] Secrets management configured
  - [ ] API keys stored securely (Vault/Sealed Secrets/CSI driver)
  - [ ] Not committed to version control
  - [ ] Access restricted via RBAC
  - [ ] Rotation policy defined

- [ ] Resource limits defined
  - [ ] Memory requests/limits set (256Mi-1Gi)
  - [ ] CPU requests/limits set (250m-1000m)
  - [ ] Adjusted based on load testing
  - [ ] Node capacity verified

### Security

- [ ] Container image scanned for vulnerabilities
- [ ] Non-root user configured (UID 1000)
- [ ] SecurityContext enforced in pod spec
- [ ] Network policies defined
- [ ] RBAC roles created with minimal permissions
- [ ] Pod Security Standards compliance verified
- [ ] TLS/SSL certificates provisioned (if applicable)

## Deployment

### Initial Deployment

- [ ] Apply ConfigMap and Secrets
  ```bash
  kubectl apply -f kubernetes/configmap.yaml
  ```

- [ ] Deploy application
  ```bash
  kubectl apply -f kubernetes/deployment.yaml
  ```

- [ ] Create services
  ```bash
  kubectl apply -f kubernetes/service.yaml
  ```

- [ ] Configure autoscaling
  ```bash
  kubectl apply -f kubernetes/hpa.yaml
  ```

### Verification

- [ ] Pods are running
  ```bash
  kubectl get pods -l app=memory-indexer
  ```

- [ ] Health checks passing
  ```bash
  kubectl port-forward svc/memory-indexer 8081:8081
  curl http://localhost:8081/health/startup
  curl http://localhost:8081/health/ready
  curl http://localhost:8081/health/live
  ```

- [ ] Test each tier health
  ```bash
  curl http://localhost:8081/health/tier/recently
  curl http://localhost:8081/health/tier/working
  curl http://localhost:8081/health/tier/session
  curl http://localhost:8081/health/tier/user
  ```

- [ ] Logs are being written
  ```bash
  kubectl logs -f deployment/memory-indexer
  ```

- [ ] Metrics are being collected
  ```bash
  kubectl get --raw /metrics
  ```

- [ ] HPA is functioning
  ```bash
  kubectl get hpa memory-indexer
  kubectl top pods -l app=memory-indexer
  ```

### Functional Testing

- [ ] Store memory via MCP tools
- [ ] Recall memories with vector search
- [ ] Verify 4-tier promotion flow:
  - [ ] Recently Buffer → Working Memory
  - [ ] Working Memory → Session Store
  - [ ] Session Store → User Profile
- [ ] Test memory pressure handling
- [ ] Verify lazy embedding loading (if enabled)
- [ ] Check contradiction detection
- [ ] Validate graph operations

## Post-Deployment

### Monitoring Setup

- [ ] Prometheus scraping configured
  - [ ] ServiceMonitor created
  - [ ] Metrics endpoint accessible
  - [ ] Scrape interval set (15s recommended)

- [ ] Grafana dashboards imported
  - [ ] Health status dashboard
  - [ ] Memory pressure dashboard
  - [ ] 4-tier metrics dashboard
  - [ ] Performance dashboard

- [ ] Alerts configured
  - [ ] Pod restart alerts
  - [ ] Health check failure alerts
  - [ ] High memory pressure alerts
  - [ ] Error rate alerts
  - [ ] Latency alerts (p95/p99)

- [ ] Logging aggregation configured
  - [ ] Logs forwarded to centralized system
  - [ ] Log retention policy set
  - [ ] Search and filtering working

### Backup and DR

- [ ] Backup schedule configured
  - [ ] Vector database backups
  - [ ] Configuration backups
  - [ ] Backup retention policy
  - [ ] Backup storage location

- [ ] Disaster recovery plan documented
  - [ ] RTO (Recovery Time Objective) defined
  - [ ] RPO (Recovery Point Objective) defined
  - [ ] Restoration procedure tested
  - [ ] Failover procedure documented

### Performance Tuning

- [ ] Load testing completed
  - [ ] Expected load simulated
  - [ ] Peak load tested
  - [ ] Sustained load tested
  - [ ] Performance metrics captured

- [ ] Resource optimization
  - [ ] Memory usage profiled
  - [ ] CPU usage profiled
  - [ ] Embedding cache hit rate measured
  - [ ] GC metrics analyzed

- [ ] Capacity planning
  - [ ] Current capacity documented
  - [ ] Growth projections calculated
  - [ ] Scaling thresholds defined
  - [ ] Cost optimization reviewed

## Operational Readiness

### Documentation

- [ ] Deployment architecture documented
- [ ] Configuration reference created
- [ ] Runbook for common operations
- [ ] Troubleshooting guide available
- [ ] Escalation procedures defined

### Team Readiness

- [ ] Operations team trained
- [ ] On-call rotation established
- [ ] Access permissions granted
- [ ] Communication channels set up
- [ ] Incident response plan reviewed

### Compliance

- [ ] Data privacy requirements met
- [ ] Compliance standards verified (SOC2, GDPR, etc.)
- [ ] Audit logging enabled
- [ ] Data retention policies implemented
- [ ] Security audit completed

## Environment-Specific Checklists

### Development

- [ ] Single replica sufficient
- [ ] Resource limits relaxed
- [ ] Debug logging enabled
- [ ] Auto-scaling disabled
- [ ] Mock providers acceptable

### Staging

- [ ] Production-like configuration
- [ ] 2-3 replicas for HA
- [ ] Resource limits production-equivalent
- [ ] Moderate logging level
- [ ] Real providers used
- [ ] Load testing performed

### Production

- [ ] High availability (3+ replicas)
- [ ] Production resource limits
- [ ] Information-level logging
- [ ] Auto-scaling enabled
- [ ] Real providers only
- [ ] All monitoring active
- [ ] Backup/DR configured
- [ ] Security hardened

## Rollback Plan

- [ ] Previous version tagged and accessible
- [ ] Rollback procedure documented
- [ ] Database migration rollback tested (if applicable)
- [ ] Configuration rollback procedure
- [ ] Smoke tests for rolled-back version

## Continuous Monitoring

### Week 1

- [ ] Monitor daily:
  - [ ] Pod restarts
  - [ ] Health check failures
  - [ ] Error rates
  - [ ] Memory pressure events
  - [ ] Performance metrics

- [ ] Review logs daily
- [ ] Check HPA behavior
- [ ] Validate backup execution

### Month 1

- [ ] Weekly capacity review
- [ ] Monthly cost analysis
- [ ] Performance trend analysis
- [ ] Security patch review
- [ ] Dependency updates

### Ongoing

- [ ] Quarterly disaster recovery drills
- [ ] Semi-annual security audits
- [ ] Continuous performance optimization
- [ ] Regular team training updates
- [ ] Documentation updates

## Success Criteria

Deployment is successful when:

- ✅ All health checks passing consistently
- ✅ Zero pod restarts for 24 hours
- ✅ p95 latency < 100ms
- ✅ Error rate < 0.1%
- ✅ Memory pressure stays in Low/Medium range
- ✅ HPA scaling working as expected
- ✅ Backup/restore tested successfully
- ✅ Monitoring alerts functional
- ✅ On-call team can handle common issues

## Common Issues and Solutions

### Issue: Pods failing startup probe

**Symptoms**: Pods restart repeatedly, never become ready

**Check**:
```bash
kubectl describe pod <pod-name>
kubectl logs <pod-name>
```

**Solutions**:
- Increase startupProbe failureThreshold (current: 12)
- Verify vector database connectivity
- Check embedding provider accessibility
- Ensure secrets are correctly configured

### Issue: High memory pressure

**Symptoms**: Memory usage > 90%, frequent evictions

**Check**:
```bash
curl http://localhost:8081/health/tier/working
kubectl top pods -l app=memory-indexer
```

**Solutions**:
- Enable lazy embedding loading
- Reduce Working Memory capacity
- Increase pod memory limits
- Scale horizontally (more replicas)

### Issue: HPA not scaling

**Symptoms**: Load increases but pod count stays the same

**Check**:
```bash
kubectl get hpa memory-indexer
kubectl describe hpa memory-indexer
kubectl get deployment metrics-server -n kube-system
```

**Solutions**:
- Verify metrics-server is running
- Check resource requests are set
- Review HPA metrics calculation
- Validate metric availability

## Sign-off

- [ ] DevOps Lead: _______________
- [ ] Security Team: _______________
- [ ] Product Owner: _______________
- [ ] Engineering Lead: _______________

**Deployment Date**: _______________
**Environment**: _______________
**Version**: v0.3.0
