# Proje: virtual-device-gateway

## Genel Bağlam
- Repo: `aysusozenn/virtual-device-gateway` (GitHub)
- Amaç: Proje .NET 10 / C# 12'den .NET 7'ye geriye taşınıyor (migration)
- Sebep: Şirket ortamı kısıtlı — Visual Studio 2022 (v17.4) ve .NET 7.0 SDK zorunlu, bu değiştirilemez
- Şirket internet erişimi kısıtlı; paketler internal Nexus repository üzerinden çekiliyor

## Migration Durumu
- Kod ve proje dosyası (.csproj) değişiklikleri tamamlandı
- Değiştirilmiş proje bir zip olarak paketlendi: `virtual-device-gateway-net7.zip`
- **Aktif blocker**: NuGet paket restore
  - Nexus repo başarıyla yapılandırıldı
  - Ancak Nexus'un nuget.org'u proxy'leyip proxy'lemediği belirsiz
  - Eğer proxy değilse şu paketler eksik kalabilir: `SharpPcap`, `PacketDotNet`, `Microsoft.Extensions.*`
- **Sıradaki adım**: IT ile konuşup Nexus repo'nun gerçek bir proxy mi yoksa sadece internal paket host'u mu olduğunu doğrulamak

## Hedef Ortam Kısıtlamaları (deployment/şirket ortamı için — değiştirilemez)
Bu kısıtlamalar projenin **çalışacağı** ortam için geçerli, geliştirme yapılan bilgisayar için değil:
- Visual Studio 2022 (v17.4)
- .NET 7.0 SDK
- Kısıtlı/kontrollü internet erişimi
- Paket kaynağı: internal Nexus repository

> Not: Kişisel/geliştirme bilgisayarında VS ve .NET SDK versiyonları tutarlılık için AYNI tutulur (VS 2022 v17.4, .NET 7.0 SDK). Tek fark: internet erişimi kısıtlı değildir, paketler doğrudan nuget.org'dan çekilebilir ve Nexus'a bu makinede ihtiyaç yoktur. Detaylar için `CLAUDE.local.md`'ye bak.

## Çalışma Kuralları
- Değişiklik önerirken .NET 7 ile uyumluluğu her zaman kontrol et (C# 12 özelliklerini kullanma) — bu, geliştirme makinesinde daha yeni SDK kurulu olsa da geçerli
- Nexus/paket erişilebilirliği sadece hedef ortam için önemli; geliştirme makinesinde paketleri doğrudan nuget.org'dan çekebilirsin, ama önerilen paketin nihayetinde Nexus üzerinden de erişilebilir olması gerektiğini unutma (bkz. "Bilinen Riskler")
- Büyük/yıkıcı değişiklikler (dosya silme, geri alınamaz git işlemleri) önerilmeden önce onay iste
- Mevcut kodu değiştirmeden önce oku ve mevcut yapıyı koru; kapsamlı yeniden yazımlardan kaçın

## Bilinen Riskler / Gotcha'lar
- Nexus'un nuget.org proxy desteği doğrulanmadı — bu doğrulanana kadar paket restore adımları tam güvenilir değil
- `SharpPcap` / `PacketDotNet` gibi native/platform bağımlı paketlerin .NET 7 uyumluluğu ayrıca kontrol edilmeli
