# ADR 0003: API Gateway Security Boundary

## Status
Accepted

## Context
Dış dünyaya açılan endpointlerde merkezi auth/rate limit kontrolü gerekir.

## Decision
- YARP tabanlı `ApiGateway` kullanılır.
- Order ve inventory route'ları JWT auth ile korunur.
- IP bazlı fixed-window rate limiting gateway katmanında uygulanır.

## Consequences
- Güvenlik ve trafik kontrolü tek noktadan yönetilir.
- Servislerin güvenlik sorumluluğu azalır.
- Gateway ayarlarının doğru yönetimi operasyonel kritik hale gelir.
