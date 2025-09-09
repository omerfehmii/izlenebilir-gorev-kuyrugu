#!/usr/bin/env python3
"""
RabbitMQ Priority Queue Setup Script
AI-optimized task queue infrastructure için priority queue'ları oluşturur
"""

import pika
import json
import sys
import time
from typing import Dict, Any

# RabbitMQ Connection Settings
RABBITMQ_HOST = "localhost"
RABBITMQ_PORT = 5672
RABBITMQ_USER = "admin"
RABBITMQ_PASSWORD = "admin123"
RABBITMQ_VHOST = "/"

# Priority Queue Configuration
PRIORITY_QUEUES = {
    "critical-priority-queue": {
        "max_priority": 255,
        "ttl": 60000,        # 1 minute
        "max_length": 1000,
        "routing_key": "priority.critical"
    },
    "high-priority-queue": {
        "max_priority": 200,
        "ttl": 300000,       # 5 minutes
        "max_length": 5000,
        "routing_key": "priority.high"
    },
    "normal-priority-queue": {
        "max_priority": 100,
        "ttl": 600000,       # 10 minutes
        "max_length": 10000,
        "routing_key": "priority.normal"
    },
    "low-priority-queue": {
        "max_priority": 50,
        "ttl": 1800000,      # 30 minutes
        "max_length": 20000,
        "routing_key": "priority.low"
    },
    "batch-queue": {
        "max_priority": 10,
        "ttl": 3600000,      # 1 hour
        "max_length": 50000,
        "routing_key": "priority.batch"
    },
    "anomaly-queue": {
        "max_priority": 150,
        "ttl": 300000,       # 5 minutes
        "max_length": 2000,
        "routing_key": "anomaly.detected"
    }
}

# Exchanges
EXCHANGES = {
    "priority-exchange": {
        "type": "topic",
        "durable": True
    },
    "anomaly-exchange": {
        "type": "direct", 
        "durable": True
    },
    "dlq-exchange": {
        "type": "direct",
        "durable": True
    }
}

# Dead Letter Queue
DLQ_QUEUE = {
    "name": "dlq-queue",
    "durable": True,
    "routing_key": "failed"
}

def wait_for_rabbitmq(max_attempts=30):
    """RabbitMQ'nun hazır olmasını bekle"""
    for attempt in range(max_attempts):
        try:
            credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASSWORD)
            parameters = pika.ConnectionParameters(
                host=RABBITMQ_HOST,
                port=RABBITMQ_PORT,
                virtual_host=RABBITMQ_VHOST,
                credentials=credentials
            )
            connection = pika.BlockingConnection(parameters)
            connection.close()
            print("✅ RabbitMQ bağlantısı başarılı!")
            return True
        except Exception as e:
            print(f"⏳ RabbitMQ bekleniyor... (deneme {attempt + 1}/{max_attempts})")
            time.sleep(2)
    
    print("❌ RabbitMQ bağlantısı kurulamadı!")
    return False

def setup_exchanges(channel):
    """Exchange'leri oluştur"""
    print("📡 Exchange'ler oluşturuluyor...")
    
    for exchange_name, config in EXCHANGES.items():
        try:
            channel.exchange_declare(
                exchange=exchange_name,
                exchange_type=config["type"],
                durable=config["durable"]
            )
            print(f"  ✅ Exchange: {exchange_name} ({config['type']})")
        except Exception as e:
            print(f"  ❌ Exchange {exchange_name} oluşturulamadı: {e}")

def setup_dlq(channel):
    """Dead Letter Queue'yu oluştur"""
    print("💀 Dead Letter Queue oluşturuluyor...")
    
    try:
        # DLQ Queue
        channel.queue_declare(
            queue=DLQ_QUEUE["name"],
            durable=DLQ_QUEUE["durable"]
        )
        
        # DLQ Exchange'e bind et
        channel.queue_bind(
            exchange="dlq-exchange",
            queue=DLQ_QUEUE["name"],
            routing_key=DLQ_QUEUE["routing_key"]
        )
        
        print(f"  ✅ DLQ: {DLQ_QUEUE['name']}")
    except Exception as e:
        print(f"  ❌ DLQ oluşturulamadı: {e}")

def setup_priority_queues(channel):
    """Priority queue'ları oluştur"""
    print("🚀 Priority queue'lar oluşturuluyor...")
    
    for queue_name, config in PRIORITY_QUEUES.items():
        try:
            # Queue arguments
            arguments = {
                "x-max-priority": config["max_priority"],
                "x-message-ttl": config["ttl"],
                "x-max-length": config["max_length"],
                "x-dead-letter-exchange": "dlq-exchange",
                "x-dead-letter-routing-key": "failed",
                "x-overflow": "reject-publish"
            }
            
            # Queue oluştur
            channel.queue_declare(
                queue=queue_name,
                durable=True,
                arguments=arguments
            )
            
            # Exchange'e bind et
            if "anomaly" in queue_name:
                exchange = "anomaly-exchange"
            else:
                exchange = "priority-exchange"
                
            channel.queue_bind(
                exchange=exchange,
                queue=queue_name,
                routing_key=config["routing_key"]
            )
            
            print(f"  ✅ Queue: {queue_name} (priority: {config['max_priority']}, max: {config['max_length']})")
            
        except Exception as e:
            print(f"  ❌ Queue {queue_name} oluşturulamadı: {e}")

def verify_setup(channel):
    """Kurulumu doğrula"""
    print("🔍 Kurulum doğrulanıyor...")
    
    try:
        # Queue'ları listele
        method = channel.queue_declare(queue='', passive=True, exclusive=True)
        
        # Her priority queue'yu kontrol et
        for queue_name in PRIORITY_QUEUES.keys():
            try:
                method = channel.queue_declare(queue=queue_name, passive=True)
                print(f"  ✅ {queue_name} - OK")
            except Exception:
                print(f"  ❌ {queue_name} - BULUNAMADI")
                
        # DLQ kontrol
        try:
            method = channel.queue_declare(queue=DLQ_QUEUE["name"], passive=True)
            print(f"  ✅ {DLQ_QUEUE['name']} - OK")
        except Exception:
            print(f"  ❌ {DLQ_QUEUE['name']} - BULUNAMADI")
            
    except Exception as e:
        print(f"  ⚠️  Doğrulama kısmen başarılı: {e}")

def print_queue_summary():
    """Queue yapılandırmasını özetle"""
    print("\n📊 Priority Queue Yapılandırması:")
    print("=" * 60)
    
    for queue_name, config in PRIORITY_QUEUES.items():
        print(f"🎯 {queue_name}")
        print(f"   Priority: {config['max_priority']}/255")
        print(f"   TTL: {config['ttl']/1000}s")
        print(f"   Max Length: {config['max_length']}")
        print(f"   Routing: {config['routing_key']}")
        print()

def main():
    """Ana setup fonksiyonu"""
    print("🐰 RabbitMQ AI-Optimized Priority Queue Setup")
    print("=" * 50)
    
    # RabbitMQ bekleme
    if not wait_for_rabbitmq():
        sys.exit(1)
    
    try:
        # Bağlantı kur
        credentials = pika.PlainCredentials(RABBITMQ_USER, RABBITMQ_PASSWORD)
        parameters = pika.ConnectionParameters(
            host=RABBITMQ_HOST,
            port=RABBITMQ_PORT,
            virtual_host=RABBITMQ_VHOST,
            credentials=credentials
        )
        
        connection = pika.BlockingConnection(parameters)
        channel = connection.channel()
        
        # Setup adımları
        setup_exchanges(channel)
        setup_dlq(channel)
        setup_priority_queues(channel)
        verify_setup(channel)
        
        # Bağlantıyı kapat
        connection.close()
        
        print("\n🎉 Priority Queue Setup Tamamlandı!")
        print_queue_summary()
        
        print("🌐 RabbitMQ Management UI: http://localhost:15672")
        print("👤 Username: admin")
        print("🔑 Password: admin123")
        
    except Exception as e:
        print(f"❌ Setup başarısız: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
