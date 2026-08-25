# Referans Analizi — kare kare, ölçülerek

Bu doküman dört referans videonun **kendisinden** çıkarıldı; önceki paketlerden
devralınan hiçbir sayı yok. Yöntem:

- `ffmpeg` ile 10 fps kare dizisi + kritik anlarda 20 fps yakınlaştırma
- Açılış karesi tam çözünürlükte (1080×1728) alınıp **bağlı bileşen analiziyle**
  ölçüldü (`hue/sat/value` maskesi → en büyük bileşenin bbox'ı)
- Tüm koordinatlar **viewport**: `x` soldan, `y` alttan, 0..1
- Ölçüm dosyaları: `.plan-build/refanalysis/measure_*.json`

Bir sayı burada yazıyorsa ölçüldü. Ölçülmediyse "tahmin" diye işaretlendi.

---

## 0 — Neden bu doküman var

Önceki turlarda üç şey aynı anda oldu:

1. Kapılar yeşil yandı ama ekranda yanlış şey vardı.
2. Bir dış paket (v4) "referanstan ölçüldü" dediği sayıları aslında önceki
   paketten devralmıştı; yazarı bunu sonradan geri çekti.
3. Ben kendi çıktı karelerime yeterince bakmadım.

Üçünün ortak çaresi aynı: **referans videodan ölçmek** ve **kendi karesine
bakmak**. Aşağıdaki her madde bu iki işten birinin sonucudur.

---

## 1 — Fit The Shape

Kaynak: `Fit The Shape.mp4` · 1080×1728 · 45 fps · 7.33 s

### 1.1 Ölçülmüş kompozisyon (t=0.00)

| Öğe | Ölçüm |
|---|---|
| Davul hücre bloğu | x `0.185..0.830` · y `0.593..0.931` |
| Tutucu plaka sırası (5 boş plaka) | y ≈ `0.521` bandı, SPIN ile aynı hizada |
| SPIN butonu | x `0.767..0.911` · y `0.481..0.559` · merkez `(0.838, 0.521)` |
| Tepsi satır 0 (turuncu baklava) | merkez `(0.374, 0.367)` |
| Tepsi satır 2 sütun 2 (pembe yıldız) | merkez `(0.626, 0.177)` |
| Alt koyu bant | tam genişlik · y `0.000..0.074` |
| Alt kilit butonu (yeşil) | merkez `(0.290, 0.100)` |

Tepsi ızgarası bu iki köşeden **3×3** olarak çözülür: sütunlar ≈ `0.374 / 0.500 /
0.626`, satırlar ≈ `0.367 / 0.272 / 0.177`.

### 1.2 Ölçülmüş etkileşim zinciri

| t (s) | Ne oluyor |
|---|---|
| 0.00 | Davul yavaşça dönüyor. Tepsi dolu (3×3). Plaka sırası **boş**. |
| ~1.10 | Oyuncu tepsideki bir şekle basıyor (ölçülen ilk olay: kırmızı altıgen) |
| ~1.15 | Şekil tepsiden ayrılıp yukarı, davula doğru yay çiziyor |
| ~1.30 | Şekil davuldaki eşleşen hücreye giriyor |
| ~1.33 | Hücre **kendi rengine dönüyor** + üzerinde beyaz kıvılcım patlaması |
| ~1.35–1.55 | **Tepsi sıkışıyor**: kalan şekiller boşalan yeri doldurmak üzere kayıyor |

### 1.3 Bizdeki sapmalar

| # | Sapma | Kanıt |
|---|---|---|
| **F1** | **Oynanabilir sıra ters.** Referansta 3×3 tepsi oynanır, 5 plaka boş durur. Bizde 5 plaka oynanır, 3×3 tepsi tamamen dekoratiftir ve hiç değişmez. | Kendi şeridimizde tepsi 16 karenin hepsinde birebir aynı |
| **F2** | **Boşalan yer dolmuyor.** Referansta tepsi sıkışır; bizde reflow yanlış sıraya (plakalara) uygulanıyor. | `DeckReflow` plaka sırasına bağlı |
| **F3** | **Efekt neredeyse görünmez.** Referansta yerleşen hücrede belirgin beyaz kıvılcım patlaması var; bizde birkaç piksellik sarı nokta ve ince bir çizgi. | Şerit kare 3–5 |
| **F4** | Level/gear, SPIN ve alt kilit bandı ekranda yok. Referansta üçü de var ve kadrajın değer haritasının önemli parçası. | Ölçülmüş konumlar §1.1 |
| **F5** | Davul hücreleri referanstakinden daha soluk ve üzerlerindeki şekil ikonu zayıf. | Referans hücrelerinde koyu, yüksek kontrastlı glif |

---

## 2 — Block Hole

Kaynak: `Block Hole.mp4` · 1080×1728 · 130 fps · 14.45 s

### 2.1 Ölçülmüş kompozisyon (t=0.00)

| Parça | bbox | merkez |
|---|---|---|
| Kırmızı L **blok** | x `0.115..0.446` · y `0.644..0.781` | `(0.280, 0.713)` |
| Kırmızı L **delik** | x `0.556..0.780` · y `0.229..0.365` | `(0.667, 0.297)` |
| Yeşil **blok** | x `0.111..0.333` · y `0.511..0.646` | `(0.222, 0.579)` |
| Yeşil **delik** | x `0.556..0.883` · y `0.634..0.769` | `(0.719, 0.702)` |
| Cyan dikey bar (blok+delik) | x `0.774..0.889` · y `0.234..0.634` | `(0.831, 0.434)` |
| Mor **artı blok** | x `0.387..0.726` · y `0.346..0.555` | `(0.556, 0.451)` |
| Mor **artı delik** | x `0.100..0.469` · y `0.228..0.450` | `(0.284, 0.339)` |

### 2.2 Ölçülmüş etkileşim zinciri

| t (s) | Ne oluyor |
|---|---|
| 0.00–0.70 | Oyuncu **mor artı bloğu** sürüklüyor; blok kalkık, altında gölge var, sola gidiyor |
| ~0.75 | Artı, sol-alttaki mor artı deliğine oturuyor |
| ~0.80–1.10 | Blok **yerinde** mor kristal parçalara ayrılıyor — deliğin kendi ayak izini terk etmiyor |
| ~1.10–1.90 | Parçalar **aşağı**, çukura doğru çöküyor ve indikçe koyulaşıyor |
| ~1.95 | Delik kapanıyor, tahta o hücrelerde boş |
| ~2.40 | Sıradaki etkileşim: yeşil kare sağ-üstteki yeşil deliğe |

### 2.3 Bizdeki sapmalar

| # | Sapma | Kanıt |
|---|---|---|
| **B1** | **Parçacıklar başka yerden geliyor.** Kırılma bulutu bloğun çözülmüş art bounds'ından doğuyordu; delikten bir buçuk hücre uzakta başlayıp tahtanın üzerinden geri sürükleniyordu. | Kendi şeridimiz kare 8–14: kırmızı kütle sol-alttan çıkıp sağ-üste yürüyor |
| **B2** | **Parçacıklar yukarı gidiyor.** `Velocity = out3 + Vector3.up * riseSpeed` — parçalara yukarı ilk hız veriliyordu. Referansta ilk kareden itibaren aşağı. | `BlockShatterSink.cs` |
| **B3** | Huni gecikmesi (`t-0.06`) parçaların önce dışarı saçılıp sonra deliğe geri çekilmesine sebep oluyordu. Referansta dışarıdan deliğe dönen tek bir parça yok. | aynı dosya |
| **B4** | **Yanlış parça oynanıyor.** Referansın kahraman hamlesi **mor artı → mor artı delik**. Bizde 1 hücrelik kırmızı kare oynanıyor, kırılma da o yüzden referanstakinin çok altında bir kütle. | §2.1 ölçümü |
| **B5** | Delikler ince renkli çizgi + siyah iç olarak okunuyor; referansta kalın renkli dudak + derinlik gradyanı olan gerçek çukur. | referans kareler |
| **B6** | Üst level/timer/reset paneli ve alt booster/gear bandı yok. | referans kareler |

**B1/B2/B3 düzeltildi** (bu turda): bulut deliğin XZ'sine kilitlendi, ilk hız
aşağı çevrildi, huni penceresi kaldırıldı. B4/B5/B6 açık.

---

## 3 — Stickerdom

Kaynak: `Stickerdom.mp4` · 1080×1728 · 120 fps · 9.15 s

### 3.1 Ölçülmüş etkileşim zinciri

| t (s) | Ne oluyor |
|---|---|
| 0.75 | Sayfada gri/beyaz kedi çıkartması duruyor; oyuncu ona basıyor |
| 0.80–1.05 | Çıkartma **soyuluyor**; kıvrılırken **beyaz arka yüzü** görünüyor |
| 1.05–1.25 | Sayfa üzerinde arkasında sarı-yeşil kıvılcım izi bırakarak sol-üste uçuyor |
| 1.30–1.55 | Hâlâ beyaz olarak sol-üstteki hedef kartına ulaşıyor |
| 1.55–1.75 | Dönüp küçülüyor; kart **kedi görseliyle doluyor**, yeşil "Cat" etiketi ve `1/5` sayacı beliriyor |

Yani soyma → uçuş → yapışma zinciri **bizde doğru**. Sapmalar görsel.

### 3.2 Bizdeki sapmalar

| # | Sapma |
|---|---|
| **S1** | Referans sayfası **çizilmiş bir sahne** (ramen dükkânı): yerleşmiş renkli çıkartmalar + henüz doldurulmamış yerler **koyu siluet ve `?` işareti** olarak duruyor. Bizde rastgele üst üste binmiş karikatür çıkartma kolajı var. |
| **S2** | Uçuş boyunca kıvılcım izi yok. |
| **S3** | Yapışmada kart etiket + sayaç kazanmıyor. |
| **S4** | Üst level/kalp/gear ve alt ahşap alet bandı yok. |

---

## 4 — Buca

Kaynak: `Buca.mp4` · 1080×1728 · 51 fps · 4.45 s

### 4.1 Ölçülmüş kompozisyon (t=0.00)

| Öğe | Ölçüm |
|---|---|
| Kemer ray silueti | x `0.028..0.970` · y `0.311..0.777` |
| Yeşil blok merdiveni | x `0.087..0.346` · y `0.336..0.463` |
| Altın puck (pad üzerinde) | x `0.781..0.822` · y `0.355..0.366` · merkez `(0.801, 0.361)` |
| HUD mor rozet (sol) | merkez `(0.222, 0.909)` |
| HUD mor rozet (sağ) | merkez `(0.778, 0.909)` |
| HUD altın coin pip sırası | y `0.912`, x ≈ `0.29..0.71`, adım `0.0855` |

Kemer ve merdiven ölçümleri bizim mevcut hedeflerimizle **birebir** uyuşuyor
(`RefRim` `0.028/0.970/0.311/0.777`, `RefStack` `0.087/0.344/0.336/0.464`).
Puck'ta `y` 0.009 fark var (bizde `0.352`, ölçüm `0.361`).

### 4.2 Ölçülmüş etkileşim zinciri

| t (s) | Ne oluyor |
|---|---|
| 0.00–0.60 | Ray **beyaz**. Puck sağ-altta pad üzerinde. İnce nişan çizgisi yukarı bakıyor |
| ~0.65 | Bırakma. Ray tamamen **cyan**'a dönüyor |
| 0.65–1.35 | Puck sağ kenardan yukarı, kemerin tepesinden dönüyor; arkasında kıvılcım izi |
| 1.35–1.55 | Sol taraftan inip yeşil merdivene çarpıyor |
| 1.55–2.10 | Çarpma noktasından **altın sikke akışı** fışkırıp sağ-yukarı, **HUD coin bar'a** uçuyor |
| 1.55–2.30 | Yeşil bloklar bütün küçük küpler hâlinde savruluyor |
| 2.30–3.40 | Enkaz rengi yeşil → hardal → kırmızı → macenta olarak değişiyor |
| ~3.55 | "LEVEL 6 COMPLETE" |

### 4.3 Bizdeki sapmalar

| # | Sapma |
|---|---|
| **U1** | Sikke akışının **hedefi yok**: referansta sikkeler HUD coin bar'a uçar, biz HUD'ı kaldırdığımız için akış boşluğa gidiyor. |
| **U2** | Puck'ın arkasında kıvılcım izi yok. |
| **U3** | Enkazın renk evrimi (yeşil→hardal→kırmızı→macenta) yok. |
| **U4** | Puck `y` konumu 0.009 yukarı alınmalı. |

---

## 5 — [İPTAL EDİLDİ / CANCELLED] Sahte HUD kararı

> [!IMPORTANT]
> Proje sahibinin kesin direktifi doğrultusunda bu karar **İPTAL EDİLMİŞTİR (CANCELLED)**.
> Hiçbir case'e (Case 1, 2, 3, 4) HUD, level/timer/coin/heart paneli, sayaç, buton veya sahte chrome eklenmeyecektir.
> Var olan HUD kodları temizlenmiştir. Değerlendirme yalnızca referans videolardaki oyun içi dünya efektleri üzerinden yapılacaktır.

---

## 6 — İş sırası

| Öncelik | İş | Durum |
|---|---|---|
| P0 | B1/B2/B3 — kırılma parçacıklarının kaynağı ve yönü | **düzeltildi** |
| P0 | F1/F2/F3 — Case 1 oynanış ve efektleri | **tamamlandı** (dokunulmaz) |
| P0 | B4 — Mor artı gerçek oynanabilir parça olsun | açık |
| P1 | S1 — Sayfa koyu siluet + `?` mantığına geçsin | açık |
| P1 | U1 — Sikke akışı sağ-üst çerçeve dışına yönlendirilsin | açık |
| P2 | S2/U2 — Uçuş kıvılcım izleri (Stickerdom & Buca) | açık |
| P2 | B5 — Delik derinlik dudağı ve çukur gradyanı | açık |
| P2 | U3 — Enkaz renk evrimi (yeşil→hardal→kırmızı→macenta) | açık |
| P2 | U4 — Buca puck `y` konumu düzeltmesi (0.361) | açık |
