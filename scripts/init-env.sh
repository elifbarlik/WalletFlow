#!/usr/bin/env bash
# Creates .env with freshly generated random secrets. Refuses to overwrite an existing file.
set -euo pipefail
cd "$(dirname "$0")/.."
if [ -e .env ]; then echo ".env already exists; not touching it." >&2; exit 1; fi

# rand <bytes of entropy> <characters to keep>
rand() { openssl rand -base64 "$1" | tr -d '/+=\n' | cut -c1-"$2"; }

demo="Demo-$(rand 24 16)9"
cat > .env <<EOF
POSTGRES_PASSWORD=$(rand 32 28)
JWT_SIGNING_KEY=$(rand 64 56)
PAYMENT_PROVIDER_API_KEY=$(rand 32 32)
RABBITMQ_PASSWORD=$(rand 32 28)
REDIS_PASSWORD=$(rand 32 28)

# Fictional demo users: admin@ / auditor@ / demo@walletflow.test
SEED_ENABLED=true
SEED_ADMIN_PASSWORD=Adm-$(rand 24 16)9
SEED_AUDITOR_PASSWORD=Aud-$(rand 24 16)9
SEED_DEMO_PASSWORD=$demo

# Optional features (use the matching compose profile)
# Idempotency store: Redis (default) or Postgres
IDEMPOTENCY_PROVIDER=Redis
# Messaging: Log (default) or RabbitMq (--profile messaging)
MESSAGING_PROVIDER=Log
OTEL_ENDPOINT=
SEQ_URL=
EOF
chmod 600 .env
echo "Created .env. Demo login: demo@walletflow.test / $demo"

