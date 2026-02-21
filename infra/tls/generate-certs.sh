#!/bin/bash
# Generate self-signed TLS certificates for Fab Copilot infrastructure.
# Usage: bash generate-certs.sh
# Output: certs/ directory with CA, server, and client certificates.

set -euo pipefail

CERT_DIR="$(dirname "$0")/certs"
DAYS=365
CA_SUBJECT="/CN=FabCopilot-CA/O=FabCopilot"
SERVER_SUBJECT="/CN=localhost/O=FabCopilot"

mkdir -p "$CERT_DIR"

echo "=== Generating CA key and certificate ==="
openssl genrsa -out "$CERT_DIR/ca.key" 4096
openssl req -new -x509 -key "$CERT_DIR/ca.key" -out "$CERT_DIR/ca.crt" \
    -days "$DAYS" -subj "$CA_SUBJECT"

echo "=== Generating server key and certificate ==="
openssl genrsa -out "$CERT_DIR/server.key" 2048
openssl req -new -key "$CERT_DIR/server.key" -out "$CERT_DIR/server.csr" \
    -subj "$SERVER_SUBJECT"

# SAN extension for localhost + Docker service names
cat > "$CERT_DIR/server-ext.cnf" <<EOF
[v3_req]
subjectAltName = @alt_names

[alt_names]
DNS.1 = localhost
DNS.2 = nats
DNS.3 = redis
DNS.4 = qdrant
DNS.5 = gateway
IP.1 = 127.0.0.1
IP.2 = 0.0.0.0
EOF

openssl x509 -req -in "$CERT_DIR/server.csr" -CA "$CERT_DIR/ca.crt" -CAkey "$CERT_DIR/ca.key" \
    -CAcreateserial -out "$CERT_DIR/server.crt" -days "$DAYS" \
    -extfile "$CERT_DIR/server-ext.cnf" -extensions v3_req

echo "=== Generating client key and certificate ==="
openssl genrsa -out "$CERT_DIR/client.key" 2048
openssl req -new -key "$CERT_DIR/client.key" -out "$CERT_DIR/client.csr" \
    -subj "/CN=FabCopilot-Client/O=FabCopilot"
openssl x509 -req -in "$CERT_DIR/client.csr" -CA "$CERT_DIR/ca.crt" -CAkey "$CERT_DIR/ca.key" \
    -CAcreateserial -out "$CERT_DIR/client.crt" -days "$DAYS"

# Clean up CSR and serial files
rm -f "$CERT_DIR"/*.csr "$CERT_DIR"/*.srl "$CERT_DIR"/*.cnf

echo ""
echo "=== TLS certificates generated in $CERT_DIR ==="
echo "  CA:     ca.crt, ca.key"
echo "  Server: server.crt, server.key"
echo "  Client: client.crt, client.key"
echo ""
echo "Usage: docker compose -f docker-compose.yml -f docker-compose.tls.yml up -d"
