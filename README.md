# İzlenebilir Görev Kuyruğu (Trackable Task Queue)

Bu proje RabbitMQ kullanarak asenkron görev işleme sistemini ve OpenTelemetry ile tam izlenebilirlik altyapısını göstermektedir.

## 🏗️ Mimari

```
Producer App → RabbitMQ → Consumer App
     ↓           ↓            ↓
OpenTelemetry Collector ← ← ← ←
     ↓           ↓
  Jaeger    Prometheus
              ↓
           Grafana
```

## 🛠️ Teknolojiler

- **.NET 6**: Producer ve Consumer uygulamaları
- **RabbitMQ**: Mesaj kuyruğu
- **OpenTelemetry**: Distributed tracing ve metrik toplama
- **Jaeger**: Trace görselleştirme
- **Prometheus**: Metrik depolama
- **Grafana**: Dashboard ve görselleştirme
- **Docker/Docker Compose**: Container orchestration

## 📋 Özellikler

- ✅ Asenkron görev işleme
- ✅ Distributed tracing (Producer → Consumer)
- ✅ Context propagation
- ✅ Metrik toplama ve izleme
- ✅ Real-time dashboard
- ✅ Error handling ve retry mekanizması
- ✅ Farklı görev türleri
- ✅ Detaylı loglama

## 🚀 Kurulum ve Çalıştırma

### 1. Docker Ortamını Başlatma

```bash
# Tüm infrastructure servislerini başlat
docker-compose up -d

# Servislerin durumunu kontrol et
docker-compose ps
```

### 2. .NET Uygulamalarını Derleme

```bash
# Producer uygulamasını derle
cd src/Producer
dotnet build

# Consumer uygulamasını derle
cd ../Consumer
dotnet build
cd ../..
```

### 3. Consumer Uygulamasını Başlatma

```bash
# Terminal 1 - Consumer'ı çalıştır
cd src/Consumer
dotnet run
```

### 4. Producer Uygulamasını Çalıştırma

```bash
# Terminal 2 - Producer'ı çalıştır
cd src/Producer
dotnet run
```

## 🔗 Web Arayüzleri

Servislerin başarıyla çalıştığını doğrulamak için aşağıdaki URL'leri ziyaret edebilirsiniz:

| Servis | URL | Açıklama |
|--------|-----|----------|
| **RabbitMQ Management** | http://localhost:15672 | Kuyruk yönetimi (admin/admin123) |
| **Jaeger UI** | http://localhost:16686 | Distributed tracing |
| **Prometheus** | http://localhost:9090 | Metrik sorguları |
| **Grafana** | http://localhost:3000 | Dashboard (admin/admin123) |

## 📊 İzleme ve Görselleştirme

### Jaeger Traces
- Producer'dan Consumer'a tam trace akışı
- Her görev için detaylı span bilgileri
- Error tracking ve performance metrikleri

### Grafana Dashboard
- Görev işleme oranları
- Mesaj kuyrugu metrikleri
- Sistem sağlık durumu
- Real-time log akışı

## 📝 Görev Türleri

Sistem 3 farklı görev türünü desteklemektedir:

### 1. ReportGeneration (Rapor Oluşturma)
- **Süre**: ~8 saniye
- **Adımlar**: Veri toplama → Analiz → Rapor oluşturma → PDF dönüştürme

### 2. DataProcessing (Veri İşleme)
- **Süre**: ~5 saniye  
- **Adımlar**: Veri doğrulama → Temizleme → Analiz

### 3. EmailNotification (E-posta Bildirimi)
- **Süre**: ~3 saniye
- **Adımlar**: Template hazırlama → E-posta gönderimi

## 🔧 Konfigürasyon

### RabbitMQ Ayarları
```
Host: localhost:5672
Management UI: localhost:15672
Username: admin
Password: admin123
Queue: task-queue
```

### OpenTelemetry Endpoint
```
OTLP Endpoint: http://localhost:4317
```

## 📈 Örnek Kullanım Senaryosu

1. **Producer** 3 farklı görev mesajı oluşturur ve RabbitMQ'ya gönderir
2. **Consumer** mesajları alır ve sırayla işler
3. **OpenTelemetry** her işlem adımını trace olarak kaydeder
4. **Jaeger** trace'leri görselleştirir
5. **Prometheus** metrikleri toplar
6. **Grafana** dashboard'da gerçek zamanlı görselleştirme sağlar

## 🐛 Hata Ayıklama

### Container Loglarını İnceleme
```bash
# Tüm servislerin loglarını göster
docker-compose logs -f

# Belirli bir servisin loglarını göster
docker-compose logs -f rabbitmq
docker-compose logs -f jaeger
```

### .NET Uygulaması Logları
```bash
# Consumer logları
cd src/Consumer && dotnet run

# Producer logları  
cd src/Producer && dotnet run
```

### Yaygın Sorunlar ve Çözümler

**Problem**: RabbitMQ bağlantı hatası
```bash
# Çözüm: RabbitMQ'nun başlatıldığını kontrol edin
docker-compose ps rabbitmq
```

**Problem**: OpenTelemetry verileri görünmüyor
```bash
# Çözüm: OTel Collector'ın çalıştığını kontrol edin
docker-compose logs otel-collector
```

## 🧪 Test Senaryoları

### Manuel Test
1. Docker servislerini başlatın
2. Consumer'ı çalıştırın
3. Producer'ı çalıştırın
4. Jaeger UI'da trace'leri kontrol edin
5. Grafana'da metrikleri görüntüleyin

### Performans Testi
```bash
# Producer'ı birden fazla kez çalıştırarak yük testi yapın
for i in {1..5}; do cd src/Producer && dotnet run && cd ../..; done
```

## 🛡️ Güvenlik Notları

- Bu demo amaçlı bir projedir
- Production'da güçlü parolalar kullanın
- Network security ayarlarını yapılandırın
- Authentication/authorization ekleyin

## 🤝 Katkıda Bulunma

1. Fork edin
2. Feature branch oluşturun (`git checkout -b feature/amazing-feature`)
3. Commit edin (`git commit -m 'Add amazing feature'`)
4. Push edin (`git push origin feature/amazing-feature`)
5. Pull Request açın

## 📄 Lisans

Bu proje MIT lisansı altındadır.

## 📞 İletişim

Sorularınız için issue açabilir veya pull request gönderebilirsiniz.

---

**Not**: İlk çalıştırmada Docker image'larının indirilmesi birkaç dakika sürebilir. 