# ADR 0002: Inventory Optimistic Concurrency

## Status
Accepted

## Context
Aynı ürün için yüksek eşzamanlı sipariş işleme sırasında stok negatif düşmemelidir.

## Decision
- `inventory` tablosunda `version` alanı tutulur.
- Stok düşümü `WHERE version = @expected_version AND available_quantity >= @quantity` koşulu ile yapılır.
- Güncelleme başarısız olursa sipariş `Rejected` olarak işaretlenir.

## Consequences
- Yarış durumlarında stok tutarsızlığı önlenir.
- Çakışma durumları deterministik şekilde ele alınır.
- Rejected sipariş oranı pik yükte artabilir; bu iş kuralı olarak kabul edilmiştir.
