# Virtual Device Gateway (emulator)

Raw-Ethernet (Layer 2 + IPv4) tabanlı **sanal cihaz ağ geçidi**. Karşı uygulamadan
(App B) gelen Ethernet/IP frame'lerini yakalar, protokole göre çözer, hedef **simüle
cihaza** yönlendirir ve cihazın cevabını yeni bir frame olarak geri gönderir. Amaç:
geliştiricinin gerçek donanım/IDE olmadan kendi kodunu Windows üzerinde doğrulaması.

## Mimari (katmanlar)

```
Gateway.Harness        Konsol: adapter listele / çalıştır / loopback self-test
Gateway.Ethernet       SharpPcap/Npcap capture+send, ARP responder, GatewayEngine
Gateway.Protocol       IProtocolCodec implementasyonu (şimdilik Passthrough; spec gelince değişir)
Gateway.Devices        SimulatedDevice + davranış stratejileri + fault injection
Gateway.Configuration  gateway.json modelleri + DeviceFactory
Gateway.Core           Çekirdek tipler/arayüzler (DeviceIdentity, IDeviceBehavior, router…)
tests/…                Davranış birim testleri (donanım gerektirmez)
```

### Veri akışı
```
RX frame → capture → kuyruk → Ethernet/IP decode
  → ARP isteği ise: cihaz adına ARP cevabı
  → IPv4 ise: DstIP ile cihaz çöz → ProtocolCodec.Decode → device.HandleAsync
  → reply → ProtocolCodec.Encode → IP/Ethernet kur (src/dst swap) → send
```

## Gereksinimler
- **.NET 10 SDK** (kurulu: 10.0.301)
- **Npcap** — https://npcap.com (canlı çalıştırma/self-test için). Kurarken:
  - ☑ *WinPcap API-compatible Mode*
  - ☑ *Support loopback traffic ("Npcap Loopback Adapter")*
  - (Aynı makinede iki uygulama L2 haberleşeceği için loopback adaptörü şart.)

## Derleme ve test
```powershell
dotnet build Emulator.slnx
dotnet test tests/Gateway.Devices.Tests/Gateway.Devices.Tests.csproj
```

## Çalıştırma
```powershell
cd src/Gateway.Harness
dotnet run -- selftest          # uçtan uca round-trip (bellek-içi bus; Npcap GEREKMEZ)
dotnet run -- list              # capture adaptörlerini listele (Npcap gerekir)
dotnet run -- run gateway.json  # cihazlarla ağ geçidini gerçek adaptörde başlat (Npcap gerekir)
```

## Transport / medyum (önemli)
Motor, `IPacketTransport` arkasında ham gönder/al medyumundan **bağımsızdır**:
- **`PcapTransport`** — SharpPcap/Npcap ile gerçek adaptör (`run`, `list`).
- **`InMemoryBus` / `InMemoryTransport`** — aynı process içinde paylaşımlı L2 segmenti
  (`selftest` ve birim testleri). OS loopback enjeksiyonuna bağlı kalmadan tam pipeline'ı
  (MAC + ARP dahil) deterministik doğrular.

Aynı makinede iki uygulamayı **gerçek raw Ethernet** ile bağlamak için:
- `\Device\NPF_Loopback` ("Adapter for loopback traffic capture") bir **Null** linktir
  (MAC/ARP yok, OS ham enjeksiyonu kısıtlar) → bizim Ethernet/MAC modelimize uymaz.
- Doğru çözüm: **Microsoft KM-TEST Loopback Adapter** (gerçek sanal Ethernet NIC).
  Her iki uygulama da ona bağlanır; TX→RX yansıdığı için MAC + ARP + IP eksiksiz çalışır
  ve mevcut kod hiç değişmeden koşar. (İki ayrı makine senaryosu da aynı kodla çalışır.)

## Durum
- [x] Faz 0 — solution iskeleti, derleniyor (0 hata)
- [x] Faz 1 — Ethernet/IP + ARP responder + uçtan uca pipeline; transport seam
- [x] Doğrulama — 8/8 test + `selftest` PASS (bellek-içi Ethernet bus)
- [ ] Canlı Npcap doğrulama — KM-TEST loopback adaptörü veya iki makine ile
- [ ] Faz 2 — gerçek protokol codec'i (spec bekleniyor)
- [ ] Faz 4 — WPF arayüzü (canlı trafik monitörü)

> **Protokol seam'i:** Gerçek frame layout'u gelene kadar `PassthroughProtocolCodec`
> kullanılıyor (IP payload'ı ham veri, command=0). Spec geldiğinde yalnızca
> `Gateway.Protocol` değişir; diğer katmanlar sabit kalır.
