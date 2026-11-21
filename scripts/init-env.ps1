# Creates .env with freshly generated random secrets. Refuses to overwrite an existing file.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root '.env'
if (Test-Path $target) { Write-Error ".env already exists; not touching it."; exit 1 }

function New-Secret([int]$Length) {
    $chars = [char[]]'abcdefghijkmnpqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789'
    $limit = 256 - (256 % $chars.Length)   # reject biased values so every character is equally likely
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $result = [System.Text.StringBuilder]::new()
    $buffer = [byte[]]::new(1)
    while ($result.Length -lt $Length) {
        $rng.GetBytes($buffer)
        if ($buffer[0] -lt $limit) { [void]$result.Append($chars[$buffer[0] % $chars.Length]) }
    }
    $rng.Dispose()
    $result.ToString()
}
$demo = "Demo-$(New-Secret 16)9"
@"
POSTGRES_PASSWORD=$(New-Secret 28)
JWT_SIGNING_KEY=$(New-Secret 56)
PAYMENT_PROVIDER_API_KEY=$(New-Secret 32)
RABBITMQ_PASSWORD=$(New-Secret 28)
REDIS_PASSWORD=$(New-Secret 28)

# Fictional demo users: admin@ / auditor@ / demo@walletflow.test
SEED_ENABLED=true
SEED_ADMIN_PASSWORD=Adm-$(New-Secret 16)9
SEED_AUDITOR_PASSWORD=Aud-$(New-Secret 16)9
SEED_DEMO_PASSWORD=$demo

# Optional features (use the matching compose profile)
# Idempotency store: Redis (default) or Postgres
IDEMPOTENCY_PROVIDER=Redis
# Messaging: Log (default) or RabbitMq (--profile messaging)
MESSAGING_PROVIDER=Log
OTEL_ENDPOINT=
SEQ_URL=
"@ | Set-Content -Path $target -Encoding ascii
Write-Host "Created .env. Demo login: demo@walletflow.test / $demo"


