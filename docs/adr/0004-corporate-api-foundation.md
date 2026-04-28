# ADR 0004: Corporate API Foundation

## Status
Accepted

## Context
Servislerde hata formati, correlation id tasima ve validation davranisi ortak degildi. Bu durum servis sayisi arttikca bakim maliyetini ve istemci entegrasyon riskini artirir.

## Decision
- Cross-cutting API davranislari `Shared.BuildingBlocks` altinda toplandi.
- Global exception middleware tek tip `application/problem+json` response uretir.
- `X-Correlation-Id` tum HTTP response'larinda tasinir ve log context'e eklenir.
- FluentValidation endpoint filter ile request validation merkezi hale getirildi.
- Ucretli veya guvenlik riski olan mapping paketleri yerine manuel mapping standardi tercih edildi.

## Consequences
- Yeni servisler ayni foundation extension'larini kullanarak standart davranisa katilir.
- Endpoint icindeki daginik validation kontrolleri azalir.
- Mapping kurallari acik ve test edilebilir kalir.
