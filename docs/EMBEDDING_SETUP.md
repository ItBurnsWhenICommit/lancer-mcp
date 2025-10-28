# Embedding Service Setup

This document explains how to set up the Text Embeddings Inference (TEI) service for generating code embeddings.

## Overview

The Project Indexer MCP uses **Hugging Face Text Embeddings Inference (TEI)** to generate embeddings for code chunks. TEI is a high-performance, production-ready embedding service that supports the **jina-embeddings-v2-base-code** model.

### Model Specifications

- **Model**: `jinaai/jina-embeddings-v2-base-code`
- **Dimensions**: 768
- **Context Window**: 8,192 tokens
- **Languages**: English + 30 programming languages
- **Optimized for**: Code search and semantic similarity

## Prerequisites

- Docker installed
- NVIDIA GPU with CUDA 12.2+ (for GPU acceleration)
  - Or CPU-only mode (slower but works without GPU)
- At least 4GB of free disk space for the model

## Quick Start (GPU)

### 1. Pull and Run TEI Docker Container

```bash
# Set the model
model=jinaai/jina-embeddings-v2-base-code

# Create a volume for model cache
volume=$PWD/data

# Run TEI with GPU support
docker run --gpus all -p 8080:80 -v $volume:/data --pull always \
  ghcr.io/huggingface/text-embeddings-inference:1.8 \
  --model-id $model
```

### 2. Verify the Service is Running

```bash
# Check health
curl http://localhost:8080/health

# Get model info
curl http://localhost:8080/info

# Test embedding generation
curl http://localhost:8080/embed \
  -X POST \
  -d '{"inputs":"public class HelloWorld { }"}' \
  -H 'Content-Type: application/json'
```

You should see a 768-dimensional vector in the response.

## CPU-Only Mode

If you don't have a GPU, you can run TEI in CPU mode:

```bash
model=jinaai/jina-embeddings-v2-base-code
volume=$PWD/data

docker run -p 8080:80 -v $volume:/data --pull always \
  ghcr.io/huggingface/text-embeddings-inference:cpu-1.8 \
  --model-id $model
```

**Note**: CPU mode is significantly slower than GPU mode.

## Configuration

### Project Indexer MCP Configuration

Update your `appsettings.json`:

```json
{
  "EmbeddingServiceUrl": "http://localhost:8080",
  "EmbeddingModel": "jinaai/jina-embeddings-v2-base-code",
  "EmbeddingBatchSize": 32,
  "EmbeddingTimeoutSeconds": 60
}
```

### TEI Configuration Options

You can customize TEI behavior with additional flags:

```bash
docker run --gpus all -p 8080:80 -v $volume:/data \
  ghcr.io/huggingface/text-embeddings-inference:1.8 \
  --model-id $model \
  --max-concurrent-requests 512 \
  --max-batch-tokens 16384 \
  --max-client-batch-size 32
```

**Common options:**
- `--max-concurrent-requests`: Maximum concurrent requests (default: 512)
- `--max-batch-tokens`: Maximum tokens in a batch (default: 16384)
- `--max-client-batch-size`: Maximum inputs per request (default: 32)
- `--max-batch-requests`: Maximum requests in a batch
- `--pooling`: Pooling method (cls, mean, splade, last-token)

## Running as a Background Service

### Using Docker Compose

Create `docker-compose.yml`:

```yaml
version: '3.8'

services:
  text-embeddings:
    image: ghcr.io/huggingface/text-embeddings-inference:1.8
    ports:
      - "8080:80"
    volumes:
      - ./data:/data
    command:
      - --model-id=jinaai/jina-embeddings-v2-base-code
      - --max-concurrent-requests=512
      - --max-batch-tokens=16384
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: 1
              capabilities: [gpu]
```

Run with:

```bash
docker-compose up -d
```

### Using systemd (Linux)

Create `/etc/systemd/system/text-embeddings.service`:

```ini
[Unit]
Description=Text Embeddings Inference Service
After=docker.service
Requires=docker.service

[Service]
Type=simple
Restart=always
ExecStart=/usr/bin/docker run --rm --gpus all -p 8080:80 \
  -v /var/lib/text-embeddings/data:/data \
  ghcr.io/huggingface/text-embeddings-inference:1.8 \
  --model-id jinaai/jina-embeddings-v2-base-code

[Install]
WantedBy=multi-user.target
```

Enable and start:

```bash
sudo systemctl enable text-embeddings
sudo systemctl start text-embeddings
sudo systemctl status text-embeddings
```

## Performance Tuning

### GPU Selection

If you have multiple GPUs, specify which one to use:

```bash
docker run --gpus '"device=0"' -p 8080:80 -v $volume:/data \
  ghcr.io/huggingface/text-embeddings-inference:1.8 \
  --model-id $model
```

### Batch Size Optimization

- **Larger batches**: Better GPU utilization, higher throughput
- **Smaller batches**: Lower latency, less memory usage

Recommended settings:
- **GPU (A100, A10)**: `--max-batch-tokens=32768`, `--max-client-batch-size=64`
- **GPU (T4, RTX 3000)**: `--max-batch-tokens=16384`, `--max-client-batch-size=32`
- **CPU**: `--max-batch-tokens=8192`, `--max-client-batch-size=16`

## Troubleshooting

### Container Won't Start

**Error**: `CUDA error: no kernel image is available for execution on the device`

**Solution**: Use the correct Docker image for your GPU architecture:
- Turing (T4, RTX 2000): `ghcr.io/huggingface/text-embeddings-inference:turing-1.8`
- Ampere 80 (A100, A30): `ghcr.io/huggingface/text-embeddings-inference:1.8`
- Ampere 86 (A10, A40): `ghcr.io/huggingface/text-embeddings-inference:86-1.8`
- Ada Lovelace (RTX 4000): `ghcr.io/huggingface/text-embeddings-inference:89-1.8`

### Out of Memory

**Error**: `CUDA out of memory`

**Solution**: Reduce batch size:
```bash
--max-batch-tokens=8192 --max-client-batch-size=16
```

### Slow Performance

**Check**:
1. Are you using GPU mode? (`--gpus all`)
2. Is the model cached? (First run downloads the model)
3. Is batch size too small? (Increase for better throughput)

### Connection Refused

**Check**:
1. Is the container running? `docker ps`
2. Is the port correct? (Container port 80 → Host port 8080)
3. Is the firewall blocking the port?

## Monitoring

### Check Container Logs

```bash
docker logs -f <container-id>
```

### Prometheus Metrics

TEI exposes Prometheus metrics on port 9000:

```bash
curl http://localhost:9000/metrics
```

### Health Check

```bash
# Simple health check
curl http://localhost:8080/health

# Detailed model info
curl http://localhost:8080/info | jq
```

## Alternative Models

While `jina-embeddings-v2-base-code` is recommended for code, you can use other models:

```bash
# Multilingual general-purpose
model=intfloat/multilingual-e5-large-instruct

# Smaller, faster model
model=sentence-transformers/all-MiniLM-L6-v2

# Larger, more accurate model
model=Alibaba-NLP/gte-Qwen2-7B-instruct
```

**Note**: Update `EmbeddingModel` in `appsettings.json` to match.

## References

- [TEI Documentation](https://huggingface.co/docs/text-embeddings-inference)
- [TEI GitHub Repository](https://github.com/huggingface/text-embeddings-inference)
- [jina-embeddings-v2-base-code Model Card](https://huggingface.co/jinaai/jina-embeddings-v2-base-code)
- [Supported Models List](https://github.com/huggingface/text-embeddings-inference#supported-models)

