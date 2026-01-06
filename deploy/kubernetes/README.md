# Kubernetes Deployment Guide

This directory contains Kubernetes manifests for deploying Memory Indexer MCP Server in production environments.

## Prerequisites

- Kubernetes cluster (v1.24+)
- `kubectl` configured with cluster access
- Qdrant or compatible vector database
- Ollama or OpenAI API access for embeddings
- Persistent storage provisioner

## Quick Start

### 1. Create Namespace (Optional)

```bash
kubectl create namespace memory-indexer
kubectl config set-context --current --namespace=memory-indexer
```

### 2. Configure Secrets

Edit `configmap.yaml` and replace placeholder values:

```yaml
# For OpenAI embeddings
MEMORYINDEXER__EMBEDDING__APIKEY: "sk-..."

# For Qdrant Cloud
MEMORYINDEXER__STORAGE__APIKEY: "your-qdrant-key"
MEMORYINDEXER__STORAGE__CONNECTIONSTRING: "https://xxx.qdrant.io:6334"
```

**Security Warning**: Never commit actual secrets to version control. Use:
- Kubernetes Secrets (encrypted at rest)
- External secret management (Vault, AWS Secrets Manager, Azure Key Vault)
- Sealed Secrets for GitOps workflows

### 3. Deploy to Cluster

```bash
# Apply all manifests
kubectl apply -f configmap.yaml
kubectl apply -f deployment.yaml
kubectl apply -f service.yaml
kubectl apply -f hpa.yaml

# Or apply all at once
kubectl apply -f .
```

### 4. Verify Deployment

```bash
# Check pod status
kubectl get pods -l app=memory-indexer

# Check health endpoints
kubectl port-forward svc/memory-indexer 8081:8081
curl http://localhost:8081/health/ready

# View logs
kubectl logs -f deployment/memory-indexer

# Check HPA status
kubectl get hpa memory-indexer
```

## Manifests Overview

### deployment.yaml

Defines the main application deployment with:
- **3 replicas** for high availability
- **Health probes** using Phase 18.1 endpoints:
  - `/health/startup` - Startup probe (60s timeout)
  - `/health/ready` - Readiness probe (5s interval)
  - `/health/live` - Liveness probe (10s interval)
- **Resource limits**:
  - Requests: 256Mi memory, 250m CPU
  - Limits: 1Gi memory, 1000m CPU
- **Persistent storage** for vector database (10Gi PVC)
- **Security context** (non-root user 1000)

### service.yaml

Creates two services:
- **memory-indexer** (ClusterIP): Standard service for internal access
- **memory-indexer-headless**: Headless service for StatefulSet compatibility

### configmap.yaml

Environment-specific configuration:
- **Storage**: Qdrant connection settings
- **Embeddings**: Ollama/OpenAI provider configuration
- **VCM**: 4-Tier memory architecture settings
- **Health checks**: Threshold configuration
- **Logging**: Log level settings

### hpa.yaml

Horizontal Pod Autoscaler:
- **Scale range**: 2-10 replicas
- **CPU target**: 70% utilization
- **Memory target**: 80% utilization
- **Scale-up**: Aggressive (immediate, up to 4 pods/30s)
- **Scale-down**: Conservative (5min wait, max 50%/60s)

## Environment-Specific Deployments

### Development Environment

```yaml
# deployment.yaml - Reduce resources
resources:
  requests:
    memory: "128Mi"
    cpu: "100m"
  limits:
    memory: "512Mi"
    cpu: "500m"

# hpa.yaml - Disable autoscaling or use single replica
replicas: 1
```

### Staging Environment

```yaml
# deployment.yaml - Moderate resources
replicas: 2
resources:
  requests:
    memory: "256Mi"
    cpu: "250m"
  limits:
    memory: "1Gi"
    cpu: "1000m"
```

### Production Environment

Use the provided manifests as-is, or increase:

```yaml
# deployment.yaml - Production-grade resources
replicas: 5
resources:
  requests:
    memory: "512Mi"
    cpu: "500m"
  limits:
    memory: "2Gi"
    cpu: "2000m"

# hpa.yaml - Wider scaling range
minReplicas: 3
maxReplicas: 20
```

## Cloud Provider Specifics

### Azure AKS

```bash
# Install metrics-server if not present
kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml

# Use Azure Disk for persistent storage
# In deployment.yaml PVC:
storageClassName: managed-premium

# Use Azure Load Balancer
# In service.yaml:
metadata:
  annotations:
    service.beta.kubernetes.io/azure-load-balancer-internal: "true"
```

### AWS EKS

```bash
# Install metrics-server
kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/latest/download/components.yaml

# Use EBS for persistent storage
# In deployment.yaml PVC:
storageClassName: gp3

# Use NLB for service
# Already configured in service.yaml:
service.beta.kubernetes.io/aws-load-balancer-type: "nlb"
```

### Google GKE

```bash
# Metrics-server is pre-installed

# Use GCE Persistent Disk
# In deployment.yaml PVC:
storageClassName: standard-rwo

# Use Internal Load Balancer
# In service.yaml:
metadata:
  annotations:
    networking.gke.io/load-balancer-type: "Internal"
```

## Monitoring and Observability

### Prometheus Integration

Deployment includes Prometheus annotations:

```yaml
annotations:
  prometheus.io/scrape: "true"
  prometheus.io/port: "8080"
  prometheus.io/path: "/metrics"
```

### Grafana Dashboards

Create dashboards for:
- **Health Status**: Tier health, latencies, error rates
- **Memory Pressure**: GC metrics, Working Memory utilization
- **4-Tier Metrics**: Recently/Working/Session/User tier statistics
- **Performance**: Request rates, latencies (p50/p95/p99)

### Logging

Logs are written to stdout/stderr and can be collected by:
- **Fluentd/Fluent Bit**: For centralized logging
- **Cloud provider logging** (CloudWatch, Azure Monitor, Cloud Logging)
- **ELK/EFK stack**: For on-premises deployments

```bash
# View logs with kubectl
kubectl logs -f deployment/memory-indexer

# View logs for specific container
kubectl logs -f deployment/memory-indexer -c memory-indexer

# View logs for all pods
kubectl logs -l app=memory-indexer --all-containers=true
```

## Troubleshooting

### Pods Not Starting

```bash
# Check pod events
kubectl describe pod <pod-name>

# Check startup probe failures
kubectl logs <pod-name>

# Verify ConfigMap/Secret
kubectl get configmap memory-indexer-config -o yaml
kubectl get secret memory-indexer-secrets -o yaml
```

### Health Check Failures

```bash
# Port-forward and test health endpoints
kubectl port-forward <pod-name> 8081:8081

# Test each health endpoint
curl http://localhost:8081/health/startup
curl http://localhost:8081/health/ready
curl http://localhost:8081/health/live

# Check specific tier health
curl http://localhost:8081/health/tier/working
curl http://localhost:8081/health/tier/recently
```

### Storage Issues

```bash
# Check PVC status
kubectl get pvc memory-indexer-data

# Check PV binding
kubectl get pv

# Describe PVC for events
kubectl describe pvc memory-indexer-data
```

### HPA Not Scaling

```bash
# Check metrics-server
kubectl get deployment metrics-server -n kube-system

# Check HPA status
kubectl get hpa memory-indexer
kubectl describe hpa memory-indexer

# View current metrics
kubectl top pods -l app=memory-indexer
```

## Security Best Practices

### 1. Secrets Management

**Don't** commit secrets to Git:
```yaml
# ❌ BAD
MEMORYINDEXER__EMBEDDING__APIKEY: "sk-real-key-here"
```

**Do** use external secret management:
```bash
# ✅ GOOD - Sealed Secrets
kubeseal --cert public-key.pem < secret.yaml > sealed-secret.yaml

# ✅ GOOD - External Secrets Operator
kubectl apply -f external-secret.yaml
```

### 2. Network Policies

Create NetworkPolicy to restrict traffic:

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: memory-indexer-netpol
spec:
  podSelector:
    matchLabels:
      app: memory-indexer
  policyTypes:
  - Ingress
  - Egress
  ingress:
  - from:
    - podSelector:
        matchLabels:
          app: allowed-client
    ports:
    - protocol: TCP
      port: 8080
  egress:
  - to:
    - podSelector:
        matchLabels:
          app: qdrant
    ports:
    - protocol: TCP
      port: 6334
```

### 3. RBAC

Create ServiceAccount with minimal permissions:

```yaml
apiVersion: v1
kind: ServiceAccount
metadata:
  name: memory-indexer
---
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: memory-indexer-role
rules:
- apiGroups: [""]
  resources: ["configmaps", "secrets"]
  verbs: ["get", "list"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: memory-indexer-binding
subjects:
- kind: ServiceAccount
  name: memory-indexer
roleRef:
  kind: Role
  name: memory-indexer-role
  apiGroup: rbac.authorization.k8s.io
```

## Scaling Strategies

### Vertical Scaling

Increase resources per pod:
```yaml
resources:
  requests:
    memory: "1Gi"
    cpu: "1000m"
  limits:
    memory: "4Gi"
    cpu: "4000m"
```

### Horizontal Scaling

Increase replica count or adjust HPA:
```yaml
spec:
  minReplicas: 5
  maxReplicas: 20
```

### Database Scaling

For Qdrant:
- Use distributed mode with multiple nodes
- Enable clustering for high availability
- Scale storage separately from compute

## Backup and Disaster Recovery

### Backup Strategy

```bash
# Backup Qdrant data
kubectl exec -it qdrant-0 -- /bin/sh -c "tar czf /tmp/backup.tar.gz /qdrant/storage"
kubectl cp qdrant-0:/tmp/backup.tar.gz ./backup.tar.gz

# Backup configuration
kubectl get configmap memory-indexer-config -o yaml > config-backup.yaml
kubectl get secret memory-indexer-secrets -o yaml > secrets-backup.yaml
```

### Disaster Recovery

```bash
# Restore from backup
kubectl cp ./backup.tar.gz qdrant-0:/tmp/backup.tar.gz
kubectl exec -it qdrant-0 -- /bin/sh -c "tar xzf /tmp/backup.tar.gz -C /"

# Restore configuration
kubectl apply -f config-backup.yaml
kubectl apply -f secrets-backup.yaml
kubectl rollout restart deployment/memory-indexer
```

## Performance Tuning

### Memory Optimization

Enable lazy embedding loading:
```yaml
MEMORYINDEXER__VCM__WORKINGMEMORY__LAZYEMBEDDINGLOADING: "true"
```

### Connection Pooling

For high-throughput scenarios:
```yaml
MEMORYINDEXER__STORAGE__MAXPOOLSIZE: "100"
MEMORYINDEXER__EMBEDDING__MAXCONCURRENCY: "10"
```

### Garbage Collection

Tune .NET GC for production:
```yaml
env:
- name: DOTNET_GCServer
  value: "1"  # Server GC
- name: DOTNET_GCConcurrent
  value: "1"  # Concurrent GC
- name: DOTNET_GCRetainVM
  value: "1"  # Retain VM
```

## Additional Resources

- [Health Check Implementation](../../src/MemoryIndexer.Sdk/Health/)
- [4-Tier VCM Architecture](../../docs/ARCHITECTURE.md)
- [Memory Optimization Guide](../../docs/MEMORY_OPTIMIZATION.md)
- [Production Checklist](../docs/PRODUCTION_CHECKLIST.md)
