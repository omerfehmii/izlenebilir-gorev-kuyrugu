# İzlenebilir Görev Kuyruğu

AI destekli, öncelik tabanlı görev kuyruğu sistemi. RabbitMQ + .NET 6 + OpenTelemetry ile uçtan uca izlenebilirlik, Prometheus/Grafana ile metrik takibi, Jaeger ile dağıtık iz sürme.

## Neler Sunar?
- **AI destekli önceliklendirme**: Görevler için süre, öncelik ve kuyruk tavsiyesi (ML.NET + Hybrid/Fallback)
- **Akıllı yönlendirme**: Critical/High/Normal/Low/Batch/Anomaly kuyruklarına otomatik yönlendirme
- **Tam izlenebilirlik**: Producer → RabbitMQ → Consumer hattında trace/metric/log
- **Hazır dashboard'lar**: Prometheus + Grafana ile anlık görünürlük, Alertmanager ile alarmlar
- **Web UI**: Görev gönderimi ve otomatik görev senaryolarını tetiklemek için basit arayüz

---

## Hızlı Başlangıç

1) Altyapıyı başlatın
```bash
docker-compose up -d
```

2) Servisleri geliştirme modunda çalıştırın (isteğe bağlı)
```bash
# Terminal 1 - Producer (Web UI ve API)
cd src/Producer && dotnet run

# Terminal 2 - Consumer
cd src/Consumer && dotnet run

# Terminal 3 - AI Service
cd src/AIService && dotnet run
```
Not: Docker Compose ile çalıştırdığınızda Producer 80’e map’lenir (host:8080), AI Service 80’e map’lenir (host:7043), Consumer 80’e map’lenir (host:8082).

3) Arayüzler ve araçlar
- Producer Web UI: `http://localhost:8081` (dotnet run) veya `http://localhost:8080` (Docker)
- Consumer: `http://localhost:8082`
- AI Service: `http://localhost:5178` (dotnet run) veya `http://localhost:7043` (Docker)
- RabbitMQ Management: `http://localhost:15672` (admin/admin123)
- Grafana: `http://localhost:3000` (admin/admin123)
- Jaeger: `http://localhost:16686`
- Prometheus: `http://localhost:9090`

---

## Mimarinin Özeti
```
Producer (UI+API) → RabbitMQ → Consumer
      │                 │
      └─ AI Service ───┘
             │
   OpenTelemetry Collector → Jaeger
             │
         Prometheus → Grafana
```

---

## API ve Web Uçları

- Producer API (`/api/task`):
  - `GET /api/task/types` → Desteklenen görev türleri
  - `POST /api/task/send` → Tek görev gönderimi
  - `GET /api/task/stats` → Basit istatistik
  - `POST /api/task/send-demo` → Demo görevleri gönder
- Producer AutoTask API (`/api/autotasks`):
  - `GET /api/autotasks/status` → Otomatik görev durumu
  - `POST /api/autotasks/start` body: `{ "intervalSeconds": 10, "scenario": "mixed" }`
  - `POST /api/autotasks/stop`
  - `POST /api/autotasks/test-suite` → Test paketi gönder
- Producer AI API (`/api/ai`):
  - `GET /api/ai/health` → AI Service sağlık
  - `POST /api/ai/test-prediction` → Hızlı bağlantı testi
- AI Service (`/api/prediction` ve `/api/training`):
  - `POST /api/prediction/predict` | `predict-batch` | `predict-duration` | `predict-priority`
  - `GET /api/prediction/health` | `statistics` | `version`
  - `POST /api/training/record` | `POST /api/training/retrain?minRecords=500`
- Sağlık/Metrik Uçları:
  - Producer: `/health`, `/metrics`
  - Consumer: `/health`, `/metrics`, `/stats`
  - AI Service: `/health`, `/metrics`

Örnek görev gönderimi:
```bash
curl -X POST http://localhost:8081/api/task/send \
  -H "Content-Type: application/json" \
  -d '{
    "taskType": "ReportGeneration",
    "title": "Aylık Satış Raporu",
    "description": "2025 Aralık",
    "priority": 5,
    "parameters": { "Month": "December", "Year": 2024, "Format": "PDF" }
  }'
```

Otomatik görev akışı başlatma:
```bash
curl -X POST http://localhost:8081/api/autotasks/start \
  -H "Content-Type: application/json" \
  -d '{"intervalSeconds": 10, "scenario": "mixed"}'
```

---

## 🐇 RabbitMQ Priority Kuyrukları

- Kuyruklar: `critical-priority-queue`, `high-priority-queue`, `normal-priority-queue`, `low-priority-queue`, `batch-queue`, `anomaly-queue`
- Exchange'ler: `priority-exchange` (topic), `anomaly-exchange` (direct), `dlq-exchange` (direct)
- Routing key'ler:
  - critical → `priority.critical`
  - high → `priority.high`
  - normal → `priority.normal`
  - low → `priority.low`
  - batch → `priority.batch`
  - anomaly → `anomaly.detected`
- Öncelik aralıkları (max 255):
  - critical: 255, high: 200, normal: 100, low: 50, batch: 10, anomaly: 150
- TTL ve limitler kuyruk tipine göre ayarlı; DLQ: `dlq-queue`

Kurulum betiği (lokalde priority kurulumunu doğrulamak için):
```bash
python3 scripts/setup-priority-queues.py
```

---

## AI Optimizasyonu

- Producer, gönderim öncesi AI Service'ten tahmin ister. AI yanıt verirse:
  - `CalculatedPriority`, `PredictedDurationMs`, `RecommendedQueue` ile yayın yapılır
- AI yoksa/fail olursa: kural tabanlı fallback ile öncelik/kuyruk seçilir
- AI Service gerçek ML.NET modellerini `src/AIService/ML/*.zip` konumuna kaydeder (başlangıçta sentetik veriden eğitir, varsa diskten yükler)

---

## Gözlemlenebilirlik ve Dashboard'lar

- OpenTelemetry Collector: OTLP gRPC `4317`, HTTP `4318`
- Jaeger UI: `http://localhost:16686` → servis adları: `producer-app`, `consumer-app`, `AIService`
- Prometheus: `http://localhost:9090`
- Grafana: `http://localhost:3000` (admin/admin123)
  - Hazır dashboard'lar: Executive Operations, Task Queue, Simple Task Queue, AI Model Monitoring

---

## Konfigürasyon

- Ortam dosyaları:
  - Development (dotnet run): uygulama portları `src/*/appsettings.json` üzerinden
    - Producer: `Application.Port=8081`, AI BaseUrl: `http://localhost:5178`
    - Consumer: `Application.Port=8082`
  - Docker: `docker-compose.yml` servis port eşlemelerini kullanır
    - Producer: `8080:80`, AI: `7043:80`, Consumer: `8082:80`
- OTLP endpoint (container içinde): `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317`

---

## Sorun Giderme

```bash
# Servis durumları
docker-compose ps

# Logları inceleme
docker-compose logs -f <servis-adı>

# .NET build/run
cd src/<Producer|Consumer|AIService>
dotnet build && dotnet run
```

Sık karşılaşılanlar:
- AI Service sağlık: `GET http://localhost:7043/api/prediction/health` (Docker) veya `http://localhost:5178` (dotnet run)
- RabbitMQ bağlantı hatası → Kullanıcı/şifre/port (admin/admin123, 5672) ve container'ın çalıştığını doğrulayın
- Trace görünmüyor → OTLP endpoint ve Jaeger portlarını doğrulayın

---

## Katkı
1. Fork → Branch → Commit → PR
2. Hata/öneriler için Issues açın

---

Not: İlk çalıştırmada imajlar indirileceği için birkaç dakika sürebilir.
