# ADR 0001: Outbox + Idempotent Consumer

## Status
Accepted

## Context
Sipariş oluşturma akışında veri kaybı yaşamadan event yayınlamak ve aynı mesajın birden fazla işlenmesini engellemek gerekiyor.

## Decision
- Sipariş oluşturma transaction'ı içinde `orders` ve `outbox_messages` birlikte yazılır.
- Outbox relay, `FOR UPDATE SKIP LOCKED` ile batch okuyup RabbitMQ'ya publish eder.
- Consumer tarafında `processed_messages` tablosu ile idempotency sağlanır.

## Consequences
- At-least-once teslimatta veri tutarlılığı korunur.
- Consumer tekrar çalışsa bile aynı mesaj ikinci kez işlenmez.
- Outbox tablosu için düzenli temizleme gereklidir.
