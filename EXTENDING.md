# Genişletme rehberi

Bu uygulama dört ekseni **eklenti** olarak büyütür. Hiçbirinde çekirdek dosyayı değiştirmen
gerekmez — bir sınıf yaz, bir registry'ye kaydet. Aşağıdaki her bölümde sözleşme, kayıt noktası
ve çalışan bir örnek var.

| Eksen | Arayüz | Registry / kayıt noktası |
|---|---|---|
| Alan tipi (wire type) | `IFieldType` | `FieldTypeRegistry` → uygulamada `IcdTypeCatalog.Default` |
| ICD dosya formatı | `IIcdFormatReader` | `IcdReaderRegistry` → `IcdLoader.DefaultReaders` |
| Çerçeveleme / codec | `IFrameCodec` | `CodecRegistry` → `CodecRegistry.Default` + `IcdSpec.Framing` |
| GUI editör (widget) | `editor.<ad>` DataTemplate | `App.xaml` kaynakları + alanın `widget` ipucu |

---

## 1) Yeni alan tipi

Tel üzerindeki yeni bir veri tipi (bitfield, ölçekli/engineering, sabit-nokta…). Yalnız public
`IFieldType` sözleşmesini uygula:

```csharp
public interface IFieldType
{
    string Name { get; }                                  // ICD'de yazılan ad, örn "q8_8"
    int Size { get; }                                     // tel üzerindeki byte sayısı
    double Read(ReadOnlySpan<byte> src, Endianness e);
    void   Write(Span<byte> dst, double value, Endianness e);
    (double Min, double Max)? NaturalRange { get; }       // slider sınırı; null = serbest metin
    string Format(double value);                          // etiket için temel biçim
}
```

**Çalışan örnek:** Q8.8 sabit-nokta — [src/Gateway.Peers/SampleFieldTypes.cs](src/Gateway.Peers/SampleFieldTypes.cs).
16-bit ham, değer = ham/256 (tel byte'ları görünen değerden farklı).

**Kayıt** ([IcdTypeCatalog](src/Gateway.Peers/SampleFieldTypes.cs)):

```csharp
var registry = FieldTypeRegistry.CreateDefault();   // built-in u8…f32
registry.Register(new FixedPointFieldType());       // senin tipin
var spec = IcdLoader.LoadSpec(path, registry);      // ICD artık "type": "q8_8" yazabilir
```

Uygulamada yüklenen tüm tipler `IcdTypeCatalog.Default`'tan gelir; yeni tipini oraya ekle.
Built-in'ler `FieldTypes.U8 … F32` olarak da koddan erişilebilir.

---

## 2) Yeni ICD dosya formatı

JSON dışı bir kaynaktan (XML, YAML, Excel/DBC, vendor formatı) ICD okumak:

```csharp
public interface IIcdFormatReader
{
    bool CanRead(string path);                       // genelde uzantı/içerik sniff
    IcdSpec Read(string path, FieldTypeRegistry types);
}
```

**Built-in örnek:** [JsonIcdReader](src/Gateway.Icd/JsonIcdReader.cs) — kendi parser'ını aynı
şekilde kur, alan tiplerini verilen `types` registry'sinden çöz.

**Kayıt:**

```csharp
IcdLoader.DefaultReaders.Register(new XmlIcdReader());   // en son kayıt önce denenir
var spec = IcdLoader.LoadSpec("module.xml");             // CanRead'e göre seçilir
```

---

## 3) Yeni çerçeveleme / codec

Farklı header / checksum / bit-paketleme. `IcdCodec`'in `[magic][command][seq][alanlar][crc16]`
düzeni yerine kendi düzenini koy:

```csharp
public interface IFrameCodec
{
    byte[] Encode(IcdMessage message, ushort sequence, IReadOnlyDictionary<string, double> values);
    DecodeResult TryDecode(ReadOnlySpan<byte> frame, long timestampMs = 0);
}
```

Alan gövdesini paylaşmak istersen `IFieldType.Read/Write` public'tir — sadece header/trailer'ı
kendin yaz. **Kayıt** bir framing adına bağlanır:

```csharp
CodecRegistry.Default.Register("vendor-x", spec => new MyVendorCodec(spec));
```

ICD bunu deklare eder; runtime (`PeerChannel`/`EchoDut`/tap) o codec'i `CodecRegistry.Default.Build(spec)`
ile kurar:

```json
{ "device": "abc", "framing": "vendor-x", "messages": [ … ] }
```

`framing` yoksa varsayılan `"v1"` (built-in `IcdCodec`).

---

## 4) Yeni GUI editör (widget)

Bir alana özel editör (dial, harita, bit-toggle grid). İki adım, kod gerekmez:

1. **`App.xaml`** kaynaklarına `editor.<ad>` adıyla bir `DataTemplate` ekle. DataContext bir
   `FieldEditVm`'dir (`Value`, `SliderMin/Max`, `EnumOptions`, `SelectedEnum`):

   ```xml
   <DataTemplate x:Key="editor.dial">
       <!-- FieldEditVm.Value'ye bağlı özel kontrolün -->
   </DataTemplate>
   ```

2. **ICD alanında** ipucu ver:

   ```json
   { "name": "bearing", "type": "u16", "min": 0, "max": 359, "widget": "dial" }
   ```

[FieldEditorTemplateSelector](src/Gateway.App/Converters/FieldEditorTemplateSelector.cs) `editor.dial`'i
çözer. `widget` verilmezse şekilden türetilir: enum → dropdown, sınırlı sayısal → slider, yoksa text.
Built-in editörler: `editor.slider`, `editor.enum`, `editor.text`.

---

## ICD JSON şeması

[docs/icd.schema.json](docs/icd.schema.json) ICD dosya yapısını tanımlar. Editöründe
`"$schema": "./docs/icd.schema.json"` ile bağlayıp otomatik doğrulama + tamamlama alabilirsin.
(`type` ve `widget` alanları kasıtlı serbest string'dir — yukarıdaki eklentilerle genişlerler.)
