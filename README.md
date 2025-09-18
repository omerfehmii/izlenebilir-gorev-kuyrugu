# İzlenebilir Görev Kuyruğu (Trackable Task Queue)

AI destekli, öncelik tabanlı görev kuyruğu sistemi. RabbitMQ, .NET 6, ve OpenTelemetry ile tam izlenebilirlik.

## 🏗️ Mimari

```
Producer (8081) → RabbitMQ → Consumer (8082)
     ↓              ↓            ↓
    AI Service → OpenTelemetry Collector
  (5178)           ↓        ↓
               Jaeger   Prometheus
                          ↓
                      Grafana
```

## ✨ Özellikler

- 🧠 **AI Destekli Önceliklendirme**: Görevler süre ve öncelik tahmini ile otomatik sıralanır
- 📊 **Tam İzlenebilirlik**: Producer'dan Consumer'a kadar her adım trace edilir
- 🚀 **Web Arayüzü**: Manuel görev gönderimi ve monitoring dashboard
- ⚡ **Asenkron İşleme**: RabbitMQ ile queue-based architecture
- 📈 **Real-time Monitoring**: Grafana dashboard ile canlı metriklər

## 🚀 Hızlı Başlangıç

### 1. Infrastructure'ı Başlat
```bash
docker-compose up -d
```

### 2. Uygulamaları Çalıştır
```bash
# Terminal 1 - Consumer
cd src/Consumer && dotnet run

# Terminal 2 - Producer  
cd src/Producer && dotnet run

# Terminal 3 - AI Service (opsiyonel)
cd src/AIService && dotnet run
```

### 3. Web Arayüzünü Kullan
- **Producer UI**: http://localhost:8081 (görev gönderimi)
- **Grafana**: http://localhost:3000 (admin/admin123)
- **Jaeger**: http://localhost:16686 (trace görüntüleme)

## 🎯 Görev Türleri

| Tür | Süre | Açıklama |
|-----|------|----------|
| **ReportGeneration** | ~8s | Rapor oluşturma ve PDF dönüştürme |
| **DataProcessing** | ~5s | Veri doğrulama ve analiz |
| **EmailNotification** | ~3s | E-posta template ve gönderim |
| **FileProcessing** | ~6s | Dosya işleme ve dönüştürme |
| **DatabaseCleanup** | ~42s | Veritabanı temizlik işlemleri |

## 🛠️ Teknolojiler

- **.NET 6**: Backend servisleri
- **RabbitMQ**: Mesaj kuyruğu
- **AI/ML**: Hybrid prediction model
- **OpenTelemetry**: Distributed tracing
- **Jaeger, Prometheus, Grafana**: Monitoring stack
- **Docker**: Container orchestration

## 📊 Monitoring

### Dashboard'lar
- **Executive Operations**: Üst düzey KPI'lar
- **Task Queue Dashboard**: Detaylı kuyruk metrikleri
- **Simple Dashboard**: Temel göstergeler

### Servis URL'leri
| Servis | URL | Açıklama |
|--------|-----|----------|
| Producer | http://localhost:8081 | Görev gönderimi |
| Consumer | http://localhost:8082 | Health check |
| AI Service | http://localhost:5178 | Prediction API |
| RabbitMQ | http://localhost:15672 | Kuyruk yönetimi (admin/admin123) |
| Grafana | http://localhost:3000 | Dashboard (admin/admin123) |
| Jaeger | http://localhost:16686 | Trace görüntüleme |
| Prometheus | http://localhost:9090 | Metrik sorguları |

## 🔧 Konfigürasyon

Tüm servisler environment-aware config kullanır:
- **Development**: `appsettings.json`
- **Production**: `appsettings.Production.json`

## 🐛 Sorun Giderme

```bash
# Servis durumları
docker-compose ps

# Logları inceleme
docker-compose logs -f [servis-adı]

# .NET build/run
cd src/[Producer|Consumer|AIService]
dotnet build && dotnet run
```

## 🤝 Katkıda Bulunma

1. Fork → Feature branch → Commit → Push → Pull Request
2. Issue'lar ve öneriler için GitHub Issues kullanın

---

**Not**: İlk çalıştırmada Docker image'ları indirileceği için birkaç dakika sürebilir.

## Gerçek AI (ML.NET) Modu

Bu sürümde AIService gerçek ML.NET modelleri ile tahmin yapabilir.

- Eğitim/Yükleme: Servis açılışında sentetik veriden modelleri eğitir ve `src/AIService/ML/*.zip` dosyalarına kaydeder. Modeller varsa diskten yüklenir.
- Kullanım: Producer, AI tahminlerini bu modellere göre alır; modeller hazır değilse HybridAI + kural tabanlı fallback devreye girer.
- Telemetry: OTLP endpoint için container ortamında `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317` kullanın.

### Çalıştırma

- Docker Compose ile başlatın. Prometheus/Grafana/Jaeger hazırdır.
- AIService içindeki ML modelleri otomatik oluşur. Silmek için `src/AIService/ML` klasörünü temizleyin.