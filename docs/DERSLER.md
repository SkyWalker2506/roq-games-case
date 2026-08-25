# Case 1'den çıkan dersler

Bu dosya Case 1 geliştirilirken **gerçekten yaşanmış** hataların kaydı. Her madde bir kez
canımızı yaktı; hepsi Case 2, 3 ve 4'te de aynen geçerli. Genel tavsiye değil — yaşanmış olay.

> **Harness, capture ve ölçüm dersleri ayrı dosyada:** [`DERSLER-HARNESS.md`](DERSLER-HARNESS.md).
> Orada capture giriş noktaları, gürültü tabanı ölçümü, `-quit`/`-nographics` tuzakları ve
> "devralınan tolerans" kuralı var. Bir kapı ya da metrik yazmadan önce oraya bak.

---

---

# KURAL 0 — Nesneleri kamera yokmuş gibi diz, kamerayı sonra koy

> Bu, bu dosyadaki **en ağır** kuraldır. Diğer bütün maddelerden önce gelir ve Case 2, 3, 4
> için de aynen geçerlidir.

Nesnelerin yeri kameraya bakarak belirlenmez. Önce sahne, kamera hiç yokmuş gibi, dünyada
kurulur:

- Tek bir zemin düzlemi. Aynı düzlemde duracak her şeyin Y'si **aynı**.
- Tam sayı açılar. Rotasyon 0/90/180; bir şey döndürülecekse (ör. altıgen) niyetle döndürülür,
  kameraya baksın diye değil.
- Sabit ızgara adımı. Sütun ve satır aralıkları dünya biriminde tanımlıdır.
- Mantıklı sıra: tepsi önde, slot plakaları ve SPIN onun arkasında, tahta en arkada — dünyada
  da bu sırada durur, sadece ekranda değil.

Kamera **en son** gelir ve tek işi bu sahneyi doğru kadrajlamaktır: konum, açı, FOV.

**Neden:** Case 1'de yerleşim kameradan türetildi — her nesne "ekranda şu viewport noktasına
düşsün" diye ışın atılarak yerleştirildi. Oyun görüntüsü kabul edilebilir çıktı, ama Scene
view'da sahne dağıldı: farklı Y düzlemleri, keyfi açılar, birbirinden kopuk konumlar. Bu
yaklaşımın bedeli tek bir çirkin sahne değil:

- Her düzeltme kadraja bağlı olduğu için, kamerayı bir derece oynatmak yerleşimin tamamını bozar.
- Aynı ölçek iki farklı derinlikte iki farklı boy demek olduğundan, boyut/hizalama sorunları
  sürekli geri gelir.
- Sahne başka birinin (veya senin, iki hafta sonra) elle düzenlemesine kapalı hale gelir.

**Bu bir tercih değil, sıradır.** Doğru sırayı bir kez denedim ve geri aldım — çünkü dünya
sabitlerini ölçmeden **tahmin ettim**. Sıra yanlış değildi, önkoşulu eksikti: ızgara adımı,
satır aralığı ve zemin yüksekliği referanstan ölçülmüş olmalı; sonra kamera bu dünyayı
kadrajlayacak şekilde **çözülür** (mesafe/yükseklik iteratif olarak projeksiyon hedeflerine
oturtulur).

**Diğer case'lerde durum:** Case 2 ve Case 4 temiz (kameradan konum türetme yok). Case 3 artık
tek, dokunulmamış referans karesi ile ölçülmüş alfa katmanlarından oluşan authored bir sahne;
runtime kurucusu yalnızca doğrulama ve bağlantı yapıyor, görsel yerleşimi yeniden üretmiyor.

## KURAL 0'IN BEDELİ — neden en baştan yapılmalıydı

Bu yeniden yapımın maliyeti (tek oturumda, sadece yerleşimi düzeltmek için):

| Adım | Sonuç |
|---|---|
| Dünya yerleşimi geçişi yazıldı | derleme temiz, ekran **değişmedi** |
| Sebep: geçiş sahne **kaydedildikten sonra** çalışıyordu | sıra düzeltildi |
| Tahta taşındı, ray/işaretler "katı cisim" gibi peşinden sürüklendi | ray (293.7, 182.3, 357.9) açısına düştü |
| Sebep: ray tahtaya hizalı değildi, hizalı olmayan şeyi katı sürüklemek anlamsız | ray artık tahtadan **sonra** inşa ediliyor |
| Tepsiye elle seçilmiş ızgara adımları verildi | parçalar dev gibi, plakalar iç içe |
| Sebep: tepsi **zaten** yer düzlemine ışınla doğru diziliyordu; ölçülmüş düzeni tahminle değiştirdim | o aşama kapatıldı |
| Tambur dümdüz bırakıldı | üstteki kapalı sıralar kayboldu |
| Sebep: tambur bir **makara**; hangi sıranın önde olduğu gerçek bir serbestlik derecesi | faz açısı önce veriliyor |
| Tahta elle seçilen Z'ye oturtuldu | gri plaka sırası tamamen kayboldu |
| Sebep: tahta plakaların üstüne bindi | tahtanın yeri artık plakaların arka kenarından **ölçülüyor** |

Bunların hiçbiri yeni bilgi değildi. Hepsi, en başta "önce dünya, sonra kamera" yapılsaydı hiç
doğmayacak sorunlardı.

### Kök sebep: ölçüt yanlış kurulmuştu

Neden en baştan kameradan türetildi? Çünkü **başarı ölçütünü sadece kadraj olarak tanımladım.**
Kapılar "referans karesine benziyor mu" diye ölçüyordu. Bir nesneyi viewport hedefine ışın atıp
yerleştirmek bu ölçütü anında yeşil yapar. Dünya koordinatlarını ölçen hiçbir kapı yoktu, dolayısıyla
yanlış yaklaşım **kurduğum ölçüm sistemine göre doğru görünüyordu**; ilk itiraz Scene view'a bakan
insandan geldi, sistemden değil.

> **Kural:** Ölçüt neyi ölçmüyorsa, orada sessizce bozulur. Görsel bir işte kadraj kadar **dünya**
> da ölçülmeli: tek zemin Y'si, tam sayı açılar, sıralı derinlik. Bunlar bir kapı hâline getirilirse
> yanlış yol daha ilk derlemede kırmızı verir — ki bu, iki gün sonra elle fark edilmesinden ucuzdur.

## KURAL 0'IN SONUCU — sahne otoritedir, kurucu mantigi uretir

Yerlesimi kurucuya cozdurmenin sonu geldi. Denenen her yol bir yerde yanlisti:

- **Kameradan turetmek:** kadraj kabul edilebilir, dunya sacma (tahta 16 birim havada, plakalar
  y=18.8'de, tepsi zeminin altinda ve tahtanin arkasinda).
- **Dunyadan cozmek:** dunya duzeldi ama kadraj kaydi; her sabit duzeltmesi baska bir seyi bozdu
  (plakalar tepsiye bindi, satirlar birbirine girdi, tepsi kadrajin dibine dustu).
- **Piksel hedefine oturtmak:** referansin 153 px'lik satiri 170'e cikti ve komsu parcalar
  birbirine degdi.

Musab sahneyi elle dizdi ve dakikalar icinde dogru oldu. Yeni is bolumu:

| Kim | Neyin sahibi |
|---|---|
| **Sahne** (elle) | Konum, olcek, rotasyon, hierarchy gruplari, kamera |
| **Kurucu** (kod) | Sekil kimligi, renk, prefab variant, oyuk/glif, "?" kapaklar, ray, bagalanti |

Kod tarafinda tek bayrak: `Case1SceneSetup.SceneIsAuthored`. Acikken butun yerlesim gecisleri
atlanir ve satir olcekleri **sahneden okunur** — on sira parcasinin `localScale`'i FrontScale,
arka sira parcasininki BackScale.

> **Kural:** Bir insan dakikalar icinde dogru dizebiliyorsa, yerlesim otomasyonu deger uretmiyor
> demektir. Otomasyonu, insanin tekrar tekrar yapmak zorunda kalacagi seye ayir: renk kurali,
> variant uretimi, esleme, kapi. Yerlesim bir kez yapilir ve sahnede yasar.

## BU OTURUMUN HATALARI — hepsi, kanitlariyla

Yerlesimi duzeltmek tek oturumda ~15 derleme surdu ve asagidakilerin hicbiri yeni bilgi degildi.
Kisaltmadan yaziyorum; her biri tekrar edilebilir bir hata turudur.

### 1. Olcut yanlis kurulunca yanlis yol "dogru" gorunur
Kapilar sadece KADRAJI olcuyordu. Nesneyi viewport hedefine isin atip koymak bu olcutu aninda
yesillendirir; dunya koordinatlarini olcen hicbir kapi yoktu. Yanlis yaklasim kendi olcum
sistemime gore dogru gorundu ve itiraz sistemden degil, Scene view'a bakan insandan geldi.
→ **Gorsel iste kadraj kadar dunyayi da olc** (tek zemin Y'si, tam sayi acilar, sirali derinlik).

### 2. Insanin bir kerede dogru yaptigi seyi otomatiklestirmek
Kurucunun cozdugu her yerlesim bir yerde yanlisti. Kullanici sahneyi elle dizdi ve dakikalar
icinde dogru oldu. Otomasyon burada **eksi deger** uretti.
→ **Bir kez yapilan is sahnede yasar; otomasyon tekrar tekrar yapilacak ise ayrilir.**

### 3. Piksel hedefi kovalamak
Referansin 153 px'lik satirina oturtmaya calisinca satir 170'e cikti ve komsu parcalar birbirine
degdi. Piksel, iki bagimsiz seyin (boyut ve mesafe) carpimidir; birini hedefleyip digerini serbest
birakmak salinim uretir.
→ **Dunyada olc, dunyada duzelt.** Ekran olcumu yalnizca DOGRULAMA icindir.

### 4. Kaydetmeden sonra calisan gecis
Yerlesim gecisi `EditorSceneManager.SaveScene`'den SONRA kosuyordu. Derleme temiz, log dolu, ekran
bir onceki derlemeyle **birebir ayni**. Bellekteki sahneye yapilan is diske hic yazilmadi.
→ **Yazan her pass, kaydetmeden once kosar.** "Log var ama sonuc yok" once sirayi dusundurmali.

### 5. Hizali olmayan seyi katı cisim gibi surumek
Tahtayi tasirken ray/isaretleri ayni rijit donusumle tasidim. Ray tahtayla hizali degildi; sonuc
`(293.7, 182.3, 357.9)` rotasyonu oldu.
→ **Bir seyi bir seye baglayacaksan once hizali oldugunu dogrula**; degilse tasima, yeniden uret.

### 6. Hicbir sey bulamayan dongu de "basarili" rapor eder
`GROUNDED 6` yazdi; plakalarin besi de yerinde duruyordu — dongu onlari hic bulmamisti (yanlis
ebeveynde ariyordum).
→ **Sayiyi degil, BEKLENEN sayiyi logla**; bulunan 0 ise gurultulu sekilde soyle.

### 7. Ekseni tahmin etmek (bu oturumun tekrar eden koku)
"Kameranin yukarisina en cok hizali local eksen" secimi, altigenin 30 derecelik donusuyle iki
ekseni neredeyse esitleyince sessizce ters dondu: "yukseklik" cozumu Y'ye hic dokunmadi, X/Z'yi
kucultttu. Dokum kanit: on kare `(0.97, 1.00, 0.97)`, arka altigen `(0.70, 1.00, 0.70)`.
→ **Ekseni tahmin etme.** Dunyayi duz kur; duz dunyada yukseklik Y'dir ve tahmin edilecek bir sey
kalmaz.

### 8. Kurucunun ciktisi kurucunun girdisi olunca
Ayni commit'te dort ardisik derleme: `0.0452 → 0.0395 → 0.0331 → 0.0253`. Tepsi her derlemede
kuculuyor, kucuklme hizlaniyordu. Commit'lenmis sahne bile bu dongude bir adim asagidaydi, bu
yuzden "+%20" istegi ekranda +%11 olarak indi.
→ **Iki ardisik derlemenin ayni sayiyi verdigini olc.** "Bir kere calisti" idempotentlik degildir.

### 9. Mekanik metin duzenlemesi dosya yapisini bozdu
Betikle blok tasirken metodun bir kopyasi dosyanin en basina, `using` satirlarinin ustune dustu
(CS1529) ve baska bir kesimde kamera cozucusunu bastan sildim (CS0103).
→ **Mekanik duzenlemeden sonra yapiyi dogrula** (derleme + `grep -c` ile tekil tanim sayisi).

### 10. Ayni duvara defalarca kosmak
Ayni eksende ust uste ~15 derleme yaptim. `/goal`'un kendi kurali "3 turda ilerleme yoksa DUR ve
teshisini yaz" diyor; uygulamadim.
→ **Ucuncu basarisiz turda dur, teshisi yaz, yaklasimi degistir** — ayni yaklasimla dorduncu kez
denemek devam etmek degil, donmektir.

### 11. Referansla yan yana karsilastirmayi gec yapmak
Referans kareyi kendi karemizin yanina koydugum anda gercek farklar (parca yuksekligi 153/109,
genislik 115/90, arka/on orani 0.73/0.57, SPIN'in yuvarlak olmasi, alttaki kilitli yuvalar) bir
bakista ciktı. Bunu saatler once yapabilirdim.
→ **Yan yana karsilastirma isin BASINDA yapilir**, sonunda degil.

### 12. Kullanicinin editorunu kilitlemek
Batchmode derlemesi projeyi ozel kilitle alir; kullanici editorde calisirken kosarsa editoru
kapatir. Bu oturumda derlemeleri sirayla ve ancak editor kapaliyken kosabildim.
→ **Uzun vadeli cozum:** `unity pipeline install` + `unity command` / `unity eval` ile CALISAN
editore baglan (bkz. `claude-config/docs/unity-cli.md`). Batchmode'u yalnizca CI icin sakla.

---

## 1. Sahne dosyası kalıcı durum tutar; kodu geri almak sahneyi geri almaz

Kodu `git checkout` ile geri aldım ama hata devam etti. Sebep: derleme sahneyi **yazıyor**, ve
yazdığı şey `.unity` dosyasında kalıyor.

> **Kural:** Temiz bir derleme her zaman şununla başlar:
> `git checkout -- Assets/CaseN/Scenes/<Scene>.unity`
>
> Bir düzeltme kurucuda (builder) değilse kalıcı değildir. Sahnede elle sildiğim bir nesne
> (alt banttaki pembe yıldız) bir sonraki temiz checkout'ta geri geldi.

## 2. Temizliği "üreteceğim şey"e göre yazma, "ürettiğim şey"e göre yaz

**İki kez** aynı tuzağa düştüm.

- Glifler hücrelerin altına parent'lanıyordu; temizlik ise `Case1_SunkenGlyphs` adlı **boş** kökü
  siliyordu. Hiçbir şey silmiyordu. Hücrelerde **65 × 2 = 130** bayat kopya birikmişti. Dolum
  en yenisini kapatıyor, arkadaki ikisi ekranda kalıyordu → "delik kapanmıyor".
- `EnsurePlayablePieces` sadece o an yaratacağı isimleri siliyordu. `Hexagon2` yerine `Square`
  koyunca eski `Shape_Hexagon2` sahnede kaldı, altıgen hücreyi kaptı, gerçek altıgen
  `NO_TARGET` olup tıklanamaz hale geldi.

> **Kural:** Ürettiğin nesneyi işaretle (`" (generated)"` soneki, veya bir marker component) ve
> temizliği **o işarete** göre yap. İsim listesine göre yapılan temizlik geçmişi göremez.

## 3. `Object.Instantiate` prefab bağını koparır

Prefab'tan `Instantiate` edilen nesne Hierarchy'de başıboş bir kopya olur; prefab'ı düzenlemek
onu değiştirmez. "Birini düzeltince hepsi düzelsin" hiç çalışmıyordu.

> **Kural:** Editör kodunda `PrefabUtility.InstantiatePrefab(prefab, scene)` kullan.
> Şekil/renk gibi varyasyonlar için **Prefab Variant** üret:
> `PrefabUtility.SaveAsPrefabAsset(<prefab instance>, path)` bir instance'ı kaydedince Unity
> otomatik olarak Variant yazar.

## 4. Instance'a materyal yazmak prefab'ı anlamsızlaştırır

Renkleri sahnedeki her nesneye tek tek yazıyordum. Bunlar **instance override** üretiyor ve
prefab'ı eziyor. Sonuç: aynı şeklin iki kopyası iki farklı renkte.

> **Kural:** Renk/materyal variant'ta yaşar. Instance'a yazmak, prefab'ı devre dışı bırakmaktır.

## 5. String tabanlı eşleştirme sessizce yanlış eşleşir; enum kullan

`"Shape_Hexagon2"` → token `"hexagon2"` → `"Hexagon-Hole"` ile eşleşmedi → parça rastgele bir
hücreye düştü. Derleyici bunu yakalayamaz.

> **Kural:** Şekil/tip gibi sabit kümeler `enum` olsun (`ShapeId`), dönüşümler tek yerde
> (`ShapeIds.TryParse`) ve **eşleşme bulunamadığında `false` dönsün** — sessizce varsayılana
> düşmesin.

## 6. Paralel tablolar kayar; tek kaynaktan türet

Canlı sıranın şekli bir dizide, hedef sütunu başka bir elle yazılmış eşlemedeydi ve yorumda
"bunlar senkron kalmalı" yazıyordu. Senkronu insan hafızasına bırakan her şey kayar.

> **Kural:** İkinci tabloyu yazma; birinciden türet.
> `ReferenceTargetColumn(id)` artık `LiveRowShape` dizisini tarıyor.

## 7. Nokta örneklemeli kapılar gölgelendirmeye kördür

Renk kapısı yeşilken ekran yanlış görünüyordu. Kapı hücre merkezinden tek piksel örnekliyor;
shader, gradient, kontur farkını göremiyor.

> **Kural:** Her görsel değişiklikten sonra **eşleştirilmiş ölçekte yan yana** karşılaştırma üret
> ve tek bir hücreyi yakınlaştırıp bak. "Kapı yeşil" ile "ekran doğru" farklı iddialardır.

## 8. Kapıyı yeşillendirmek için eşiği değiştirme

Renk kapısı şu an Case 1'de kırmızı (`mean dE 22.0`) çünkü paleti **bilerek** referanstan
ayırdık. Eşiği düşürmek ya da beklenen rengi güncellemek yerine kırmızı bıraktım ve sebebini
yazdım.

> **Kural:** Kırmızı bir kriteri yeşil göstermenin her yolu (eşik düşürme, testi atlama,
> "elle doğruladım" deme) yasaktır. Kriter gerçekten yanlışsa düzeltilir — ama **ayrı ve görünür**
> şekilde.

## 9. Her zaman geçen kontrol, kontrol değildir

Yazdığım özet satırı çakışma bulunsa bile "all distinct" diyordu. Onu ölçülen değeri basacak
şekilde düzelttim (`minHueSeparation=29.7 deg`) ve gerçek çakışmayı `LogError` yaptım. O kontrol
ilk koşusunda **benim görmediğim** bir çakışmayı yakaladı (turuncu ile kahraman kırmızı, 22.3°).

> **Kural:** Kontrolün kırmızı dönebildiğini en az bir kez gör. Dönemiyorsa kontrol değil, süstür.

## 10. Tahmin etme, prefab'ı ölç

Glif yerleşimini **dört kez** yanlış yaptım. Beşincide bir dump yazıp prefab'ın gerçeğine baktım:

```
root.localScale (0.870, 1.470, 1.470)   -> DÜZGÜN DEĞİL, çocuk quad'ı geriyordu
body mesh yerel merkez (0, 1.557, 0)    -> yüz orijinde DEĞİL, yerel Y 1.630'da
```

İki sebep de buradaydı. Ölçüm 10 dakika, tahminler saatler sürdü.

> **Kural:** Yerleşim/eksen sorununda önce nesneyi dump et. Tahmin döngüsüne girme.

## 11. Sayıların kaynağını etiketle

Bir yerde "referans toplam süre 0.70 s" yazıyordu; sonradan bunun **bizim kendi çekimimizin**
süresi olduğu ortaya çıktı — referanstan ölçülmemişti.

> **Kural:** Her sabitin yanına kaynağını yaz: `VIDEO_MEASURED`, `VISUAL_ESTIMATE`, `TUNED`.

## 12. Sahnedeki serileşmiş değerler C# varsayılanlarını ezer

`rippleMaxDelay`'i kodda 0.26 yaptım, log hâlâ 0.060 diyordu. Component bir kez sahneye
serileştiğinde alan değerleri kodun initializer'ını yener.

> **Kural:** Runtime alanını değiştirdiysen **sahneyi yeniden kur** (`Build`), sadece capture
> koşturma. Ya da kurucu component'i her seferinde sıfırdan yaratsın.

## 13. Unity Hub editörü geri açıp batchmode'u sessizce bloke ediyor

`-batchmode` koşuları "Multiple Unity instances cannot open the same project" ile ölüyor ya da
kilit bekleyip zaman aşımına uğruyor. Bu oturumda **en az dört kez** oldu.

> **Kural:** Batch koşusu başlamıyorsa önce `pgrep -f "MacOS/Unity -projectpath"` bak. Hub'ın
> açtığı editör (`-projectpath`, küçük harf) bizim koşumuz (`-projectPath`) değildir.

## 14. `-quit` play-mode gerektiren işleri öldürür

`Build`+`Capture`'ı `-quit` ile koştum; capture play-mode'a girmeden editör kapandı ve **eski**
kareleri ölçtüm.

> **Kural:** Capture ve play-mode kapıları `-quit` OLMADAN koşulur; `FrameStripCapture`
> `EditorApplication.Exit` ile kendisi çıkar.

## 15. Toplu (aggregate) metrik farklı kadrajlar arasında karşılaştırılamaz

Dalga genliğini "davul siluet alanı" ile ölçtüm: bizde +%1.27, referansta +%4.1 çıktı ve panikle
genliği artırmaya kalktım. Oysa iki klibin kadrajı ve çözünürlüğü farklıydı. **Tek bir hücrenin
yüksekliğini** aynı yöntemle ölçünce ikisi de ×1.053 çıktı — zaten eşitti.

> **Kural:** İki kaynağı karşılaştırırken metrik **kadrajdan bağımsız** olmalı. Aggregate değil,
> aynı nesne üzerinde aynı ölçüm.

## 16. Efekt görünmüyorsa sebep genelde tek değil

Kıvılcım hiç görünmüyordu; **dört** ayrı sebep vardı: hücrenin 0.22'si kadar küçüktü, yüzün
0.12 önünde yani bloğun **içinde** doğuyordu, parlak sarı hücrede altın rengi kayboluyordu, ve
sekans kuyruğu 0.13 s'de kesiyordu.

> **Kural:** "Görünmüyor" tek bir sebep varsayma. Boyut, konum/derinlik, kontrast, ömür —
> dördünü ayrı ayrı doğrula.

## 17. Parçacık materyali ile parçacık rengi aynı şey değil

`Case1/StarParticle` shader'ı parçacıktan **sadece alpha** alıp RGB'yi materyalden okuyor.
Parçacık sisteminin gradient'ine altın rengi yazmak hiçbir şey yapmadı.

> **Kural:** Shader'ın hangi kanalı nereden aldığını oku. Ayrıca sabit `randomSeed` her patlamayı
> birebir aynı yapar — "sayı değişsin" istiyorsan tohumu da değiştirmelisin.

## 18. SRP Batcher: her pass'te `UnityPerMaterial` birebir aynı olmalı

Pass'ler arasında CBUFFER uyuşmazlığı sessizce **magenta hata materyaline** düşürüyor.

> **Kural:** Çok pass'li shader'da her `UnityPerMaterial` bloğu aynı alanları **aynı sırada**
> içermeli.

## 19. Değişikliği tek satır yerine tek YER'de yap

Aynı `Vector3 outward = RadialOut(...)` satırı dosyada iki yerde geçiyordu; python ile yanlış
olanı değiştirdim ve süslü parantez dengesini bozdum.

> **Kural:** Otomatik düzenlemede benzersiz bir bağlam yakala veya satır indeksi + assertion
> kullan. Değişiklikten sonra derlemeyi hemen koştur.

## 20. Yarım uygulanan kural, kuralsızlıktan kötüdür

"Parça rengi hedef hücreden gelsin" kuralını yazdım ama sadece **oynanabilir** parçalara
uyguladım; dekor karolar eski slot tablosundan boyanmaya devam etti. Tepsi, canlı sıranın
öğrettiği kuralı çürütüyordu.

> **Kural:** Bir kural koyduğunda o kuralın kapsadığı **her** nesneyi kapsadığından emin ol.

---

## 21. Aynı özelliğe iki tween yazarsa, kaybedeni sessizce silinir

Tepsideki parça öne geçtiğinde büyümüyordu. Sebep "büyütme kodu yok" değildi — kod vardı ve
çalışıyordu. `Reflow` aynı anda iki tween başlatıyordu: biri ölçeği ön sıra ölçeğine
yükseltiyor, diğeri (`Squash.SquashStretch`) squash uyguluyor. Squash **başlangıçtaki** ölçeği
"dinlenme ölçeği" olarak yakalar, her karede onu temel alır ve `OnComplete`'te birebir geri
yazar. İkisi de aynı gecikme ve aynı süreyle koştuğu için son sözü hep squash söylüyordu.

Ölçüm (aynı parça, sütun 3):

| Kare | Önce | Sonra |
|---|---|---|
| 0 (arka sıra) | 65 px | 65 px |
| 45 (öne geçmiş) | **62 px** | **85 px** |
| 209 (sıfırlama) | 85 px | 85 px |

Kare 209'daki sıçrama teşhisin kendisiydi: değer doğru hesaplanıyor, sadece üzerine yazılıyordu.

> **Kural:** Bir transform özelliğinin (`localScale`, `position`) o an **tek bir sahibi** olsun.
> İki efekt aynı özelliği istiyorsa tek tween ikisini birleştirsin (taban değeri lerp et,
> deformasyonu üstüne uygula). "Yazan son kod kazanır" bir yarış koşuludur; süre değişince
> davranış da değişir.

## 22. Aynı `localScale` aynı ekran yüksekliği demek değildir

Ön sıradaki üç parça birebir aynı ölçekteydi (0.5272, 1, 0.5272) ama ekranda kare 99 px,
yuvarlak 83 px, altıgen 82 px ölçtü. Sebep basit: mesh'lerin doğal yükseklikleri farklı.
Ölçek eşitliği bir **girdi** eşitliği; oyuncunun gördüğü **çıktı** eşitliği değil.

> **Kural:** Hizalama iddiaları ekran uzayında çözülür. Her parçayı hedef yüksekliğe
> **projeksiyon ölçerek** iteratif çöz (`ProjectBounds` → `hedef / ölçülen` → ölçekle → tekrar
> ölç), ve düzeltmeyi ekranda yükseklik olarak okunan eksende yap ki ayak izi (grid) bozulmasın.
> Sonuç: 0.0450 / 0.0451 / 0.0452 — %0.4 içinde.

## 24. Ölçeği "şu an durduğu yerde" değil, "kullanılacağı yerde" çöz

Ön sıra 80/80/79 px'e oturduktan sonra bile, arkadan öne geçen parça 71 px okuyordu. Sebep
perspektif: ön ölçek parçanın **arka sıradaki** (kameraya daha yakın) konumunda çözülmüştü,
ama o ölçek **ön sırada** (daha uzakta) kullanılıyordu. Aynı ölçek, iki farklı mesafede iki
farklı boy demek.

Çözüm: parçayı geçici olarak kendi sütununun ön yuvasına taşı, ön ölçeği orada çöz, sonra
yerine geri koy; arka ölçeği ise dinlendiği yerde çöz. Sonuç: 80 / 80 / 79 ve öne geçen parça
da 79.

> **Kural:** Ekran uzayında çözülen her değer, o değerin **uygulanacağı** poz altında çözülür.
> Konum değişiyorsa ölçüm de değişir.

## 25. Kurucunun idempotent olmadığını ölçtük

Aynı commit'te art arda dört derleme, ön sıra yüksekliğini şöyle verdi:

```
0.0452 → 0.0395 → 0.0331 → 0.0253
```

Yani her derleme tepsiyi biraz daha küçültüyor ve küçülme hızlanıyordu. Sebep: kurucu
çözdüğü ölçeği sahneye yazıyor, bir sonraki derleme de o yazılmış sahneyi **girdi** olarak
ölçüyor. Ders #1'deki "derlemeden önce sahneyi `git checkout` ile sıfırla" kuralı işte tam
olarak bunun için var — ama o kural bir çözüm değil, bir **pansuman**.

Case 1'de ön/arka satır çözümünü sahneye yazmayı bıraktım: sahne doğal ölçekte kalıyor,
çözülmüş değerler `DeckReflow.FrontScale/BackScale` verisinde yaşıyor ve çalışma anında
uygulanıyor. Kalan geometri (tepsi genişliği, tambur, plakalar) hâlâ sahneye yazıyor; orada
protokol geçerli.

> **Kural:** Kurucunun çıktısı, kurucunun girdisi olmamalı. Olması gerekiyorsa iki ardışık
> derlemenin **aynı sayıyı** verdiğini ölç — "bir kere çalıştı" idempotentlik kanıtı değildir.

## 23. Kurucu kendi girdisini yazmamalı

Temel prefab'ların (`Round`, `Hexagon`, `Diamond`) `m_LocalScale` değeri `y: 1` iken `y: 2`
olmuş halde commit'e girmek üzereydi. Bunlar kurucunun **girdisi**; çıktısı `Prefabs/Pieces/`
altındaki variant'lar. Girdi kirlenince her yeniden derleme bir öncekinden farklı başlar ve
"aynı komut aynı sonucu verir" garantisi çöker.

> **Kural:** Kurucunun yazma izni olan dizin ile okuduğu dizin ayrı olsun. Derlemeden sonra
> `git status` ile **girdi varlıklarının değişmediğini** doğrula — çıktıların değişmesi normal,
> girdilerin değişmesi hatadır.

---

# Sahne dizilimi nasıl olmalı

Case 1'in en pahalı yapısal hatası buydu ve hâlâ tam kapanmadı: yerleşim **kameradan
türetilmiş**. Yani her nesnenin yeri "ekranda şu viewport noktasına düşsün" diye ışın atılarak
bulundu. Kadrajda doğru görünüyor, Scene view'da dağınık: farklı Y düzlemleri, eğik açılar,
mantıksız derinlikler.

Doğrusu **dünya-önce** kurmaktır:

1. Önce dünyada bir ızgara tanımla: tek bir zemin Y'si, sabit hücre adımı, tam sayı açılar
   (0/90/180). Nesneler bu ızgaraya oturur.
2. Kamerayı **sonra** yerleştir ve sadece kamerayı kadraja göre çöz (mesafe + pitch + FOV).
3. Referans videodan gelen ölçüler ızgara sabitlerine çevrilir; her nesneye ayrı ayrı
   uygulanmaz.

Bunu bir kez denedim ve geri aldım — sıra doğruydu ama kompozisyon sabitlerini **tahmin
ettim**, ölçmedim; kadraj bozuldu. Ders sıra değil, sıranın önkoşulu: dünya-önce kurmak için
önce hücre adımı, sıra aralığı ve zemin yüksekliği referanstan **ölçülmüş** olmalı.

> **Kontrol:** Yerleşim bittiğinde ön/yan/üst/izometrik render al (`Case1AngleShots`). Yandan
> bakınca aynı düzlemde olması gerekenler tek çizgide mi? Değilse kadraj seni kandırıyor.

# Hierarchy nasıl olmalı

Dağınık hierarchy sadece estetik değil; temizlik kodunun kökü bulamamasına, bayat kopyaların
birikmesine ve "sildim ama duruyor" hatalarına yol açtı.

```
Case1                     <- tek kök, konum (0,0,0)
├── View                  <- kamera, ışıklar, post
├── Board                 <- tambur, hücreler, glifler
├── Pieces                <- tepsi + oynanabilir parçalar
├── Chrome                <- UI/banner/SPIN gibi 2B okuyan şeyler
└── Systems               <- director, input, ses (görselsiz mantık)
```

Kurallar:

- Tek kök. "Bir GO altında olsun" isteği kozmetik değil: kökü taşıyınca her şey taşınır,
  sahneyi sıfırlamak tek dal silmek olur.
- Üretilen her nesne işaretli (`" (generated)"`) ve **kendi** dalının altında.
- Kök arama **özyinelemeli** olsun; ilk seviyede arayan kod yeniden düzenlemeden sonra kökü
  bulamaz ve sessizce ikinci bir kök yaratır.
- Hierarchy'yi derlemenin **en sonunda** düzenle, yoksa sonradan yaratılanlar dışarıda kalır.

# Kamera

- Kamera **tek doğruluk kaynağıdır**: hangi eksenin ekranda yukarı okunduğu kameradan türetilir.
  Case 1'de dört ayrı hata (glif yerleşimi, ripple yönü, tepsi yassılaştırma, tile yüksekliği)
  tek bir kökten geldi: "local Y yukarıdır" varsayımı. Bu prefab'larda yüzey X-Z düzlemindeydi,
  local +Y ekrana doğru bakıyordu; Y'yi ölçekleyen kod hiçbir görsel etki yapmadan "uyguladım"
  diye log yazdı.
- Doğru yöntem: nesnenin dünya eksenlerini `cam.transform.up` / `right` üzerine projekte edip
  en çok hizalanan **local** ekseni seç. Aynı kod her poz için çalışır.
- Kamerayı "düzleştirmek" (pitch'i 0'a çekmek) bir çözüm değil: denedim, kadraj **birebir aynı**
  kaldı ama tepsi bozuldu. Kadraj sorunu sanılan şey aslında yerleşim sorunuydu.
- Perspektif kazancını hesaba kat: arka sıra kameraya **daha yakın** olduğu için 0.72 çarpanı
  ekranda hiçbir fark yaratmadı (ön 109/128/110'a karşı arka 115/111/90). Derinlik hissi
  çarpanla değil, **ekranda ölçülmüş hedefle** kurulur.

# Görsel kalite — neye önem vermeli

Değerlendirme koda değil, ekrana bakıyor. Öncelik sırası (Case 1'de gerçekten fark yaratan
sıra):

1. **Kompozisyon ve kadraj.** Nesnelerin ekrandaki yeri ve oranı. En çok geri bildirim buradan
   geldi.
2. **Siluet ve boyut tutarlılığı.** Aynı rolü oynayan şeyler aynı boyda okunmalı (bkz. #22).
3. **Renk kimliği.** Tek şekil tek renk; "aynı şekil iki renk" hatası tekrar tekrar yakalandı.
   Renk variant'ta yaşar, instance'ta değil.
4. **Malzeme ve kontur.** Toon görünüm için Fresnel yetmez; ters-kabuk (inverted hull) outline
   gerekir. Gamma renk uzayında materyal değerleri ham sRGB'dir — dönüştürme yapma.
5. **Hareket/juice.** Squash-stretch, hitstop, ripple. Önemli ama yukarıdakiler yanlışken juice
   durumu kurtarmıyor; yanlış yerleşimi hızlandırıyor sadece.
6. **Ses.** Referanstan bant analizi ile çıkarılır (tık 3.5–8 kHz, gövde 500–700 Hz).

Ölçme yöntemi: renk için ΔE (CIE76), hizalama için projeksiyon kutusu, hareket için şablon
eşleme (normalize korelasyon) ile alt-piksel kayma. "Bakınca iyi görünüyor" bir ölçüm değil —
ama ölçüm de tek başına yetmez: **kapı yeşilken ekran yanlış olabilir**, o yüzden her iddia
yan yana karşılaştırma ile gösterilir.

---

## Case 2–4 için kontrol listesi

1. Derleme öncesi sahneyi `git checkout` ile sıfırla.
2. Şekil/tip kümesi varsa **enum** ile başla, string ile değil.
3. Tekrarlayan nesneler **Prefab Variant** olsun; instance'a materyal yazma.
4. Üretilen her nesneyi işaretle; temizliği işarete göre yap.
5. Her sabite kaynak etiketi (`VIDEO_MEASURED` / `TUNED`) koy.
6. Her kapının bir kez **kırmızı** dönebildiğini gör.
7. Görsel iddiayı yan yana karşılaştırma ile kanıtla; kapı yeşili yetmez.
8. Runtime alanı değiştirdiysen sahneyi yeniden kur.
9. `-quit` sadece play-mode gerektirmeyen koşularda.
10. Karşılaştırma metriği kadrajdan bağımsız olsun.
11. Bir transform özelliğinin tek sahibi olsun; iki tween aynı alana yazmasın.
12. Hizalama/boyut iddiasını ekran uzayında çöz, `localScale` eşitliğine güvenme.
13. Derlemeden sonra girdi varlıklarının değişmediğini `git status` ile doğrula.
14. Dünyayı önce ızgaraya kur, kamerayı sonra çöz — ama sabitleri önce ölç.
15. Yerleşimi ön/yan/üst render ile denetle; kadraj tek başına yalan söyler.
16. Ekran uzayı çözümünü, değerin uygulanacağı poz/konum altında yap.
17. Kurucuyu iki kez koş; iki koşu aynı sayıyı vermiyorsa idempotent değildir.

---

# Case 3'ten çıkan ek dersler

## İllüstrasyon ağırlıklı referansı prosedürel taklitle değiştirme

Etkileşimin yalnız birkaç öğeyi hareket ettirdiği, fakat ekranın büyük bölümünün ayrıntılı bir
illüstrasyon olduğu durumda en güvenilir yapı şudur:

1. Dokunma göstergesi olmayan tek ve stabil bir referans karesini arka plan olarak kullan.
2. Yalnız etkileşen öğeleri temiz alfa katmanları halinde çıkar.
3. Etkileşim sonundaki ödül/durum görselini de aynı referanstan ayrı katman olarak al.
4. Her crop için kaynak zamanı ve yarı-açık piksel ROI'sini bir manifestte sakla.

Case 3'te generic sticker ve prosedürel HUD yaklaşımı semantiği tamamen değiştirmişti. Tek base
kare + Cat/Noodle/Sweets katmanları hem kompozisyonu korudu hem gerçek curl/flight davranışını
oynanabilir bıraktı. Kaynak ve ROI manifesti
`Assets/Case3_Stickerdom/Textures/Reference/README.md` içindedir.

## Baked piksele ikinci kez grade uygulama

Referans kare zaten son renklerini içeriyorsa Volume silmek tek başına yeterli değildir. Kamera
üzerindeki post-process, HDR, anti-aliasing ve dithering bayrakları da görüntüyü tekrar işler.
Case 3'te bunlar kapatılmadan önce kaynakla aynı PNG ekranda daha koyu görünüyordu. Baked görsel
için kamera ayarlarını serialized değerlerden kontrol et; "Volume yok" varsayımına güvenme.

## Authored sahne bittikten sonra builder yalnız wiring yapar

Kurucu kamera, layout, sprite veya materyal üretirse elle düzeltilmiş sahne her testte yeniden
bozulur. Case 3 kurucusu bu yüzden şu sözleşmeye indirildi:

- Önce bütün gerekli authored nesne ve asset'leri doğrula.
- Sonra yalnız runtime referanslarını bağla ve bilinen bayat component'i temizle.
- Gate'ler sahneyi doğrudan açsın; gizlice `Build()` çağırıp sahneyi mutasyona uğratmasın.
- İki ardışık builder koşusundan sonra sahne dosyası hash'i aynı kalmalı.

## Determinizm süre raporuyla değil piksel hash'iyle kanıtlanır

Aynı `duration` ve event sayısı, partikül seed'i veya local jitter farklıysa aynı görüntü demek
değildir. Case 3'te bütün rastgelelik sabitlendi; iki bağımsız Unity capture koşusundaki 16 karenin
SHA-256 listeleri birebir karşılaştırıldı. Bundan sonra görsel deterministiklik iddiası için hem
sequence raporu hem gerçek frame hash'i kullanılmalı.

## Capture-ready hazırlığı atomik olmalı

Wall-clock kullanan capture harness, coroutine ile yapılan prewarm'ın arasına girebilir ve ilk
sekansı yarı hazırlanmış durumda başlatabilir. Hazırlık ya tek karede atomik tamamlanmalı ya da
harness'in beklediği açık bir `Ready` sözleşmesi olmalı. `yield` içeren belirsiz prewarm, ilk run
ile sonraki run arasında yakalanması zor farklar üretir.

---

# Case 4'ten çıkan ek dersler

## Fizik uçuşunu deterministik görsel sonuçtan ayır

`fixedDeltaTime = 0.01` ve `Time.captureFramerate = 100` tek başına çok gövdeli PhysX sonucunu
tekrarlanabilir yapmadı. Puck iki pass'te aynı yolu izlese bile üst üste duran 21 blok, aynı anda
iki temastan hangisinin önce çözüldüğüne göre bambaşka yığınlara ayrıldı. Transform resetlemek,
gövdeleri uyutmak ve collider'ları kapatıp açmak temas geçmişini temizledi; fakat kaotik zinciri
deterministik yapmadı.

Case 4'te oyuncu girdisi, ray sekmeleri ve ilk stack teması hâlâ gerçek Rigidbody/solver üzerinden
gidiyor. Gerçek temas anından sonra referansta görülen bütün-blok fan-out, yerel integer hash ile
üretilen kinematik bir cascade'e devrediliyor. Böylece oynanışın sebep-sonuç ilişkisi korunurken
measurement/replay aynı kareyi veriyor.

> **Kural:** Kabul kriteri ekrandaki belirli bir sonuçsa, kaotik fiziği o sonucun tek otoritesi
> yapma. Gerçek fiziği temas/trigger'a kadar kullan; görsel climax'i ölçülmüş ve seed'li bir akışla
> üret.

## Rapor süreleri sonuç determinismi değildir

İlk capture'larda beş phase'in adı ve süresi eşleştiği halde stack sonucu `21/17 moved` ile
`16/10 moved` arasında değişiyordu. Rapor ancak sonuna gerçek sonuç imzası eklendiğinde hatayı
yakaladı: moved, rotated, maksimum yer değiştirme ve bounce sayısı.

> **Kural:** Replay kapısı yalnız zaman çizelgesini değil, görsel sonucu doğuran durum özetini de
> karşılaştırmalı. Aksi halde iki farklı oyun durumu aynı "REPORT_MATCH" satırını üretir.

## Sabit FPS'te süreyi float sınırıyla değil kare sayısıyla bekle

`while (Time.time - start < 1.50f)` bazı Unity oturumlarında 150, bazılarında 151 kare sürdü;
sebep float toplama sınırında bir mikro-saniyelik farktı. Bu fark strip toplamını `4.00 ↔ 4.01`
saniye oynatıp bir örnekleme karesini başka bir görsel ana taşıdı. Capture modunda süreler artık
`round(seconds * captureFramerate)` adet `yield` ile sayılıyor; normal oyunda scaled clock devam
ediyor.

> **Kural:** Deterministik capture saatinde zaman aralığı değil tam kare sayısı otoritedir.

## `Camera.Render`, Screen Space Overlay UI'ı çekmez

HUD oyunda görünürken frame-strip'te tamamen yoktu. `Camera.Render()` yalnız kameranın render
ettiği katmanları alır; `ScreenSpaceOverlay` canvas daha sonra compositing'e girer. Buca HUD'ı
`ScreenSpaceCamera` olarak, `GraphicRaycaster` olmadan ve bütün `raycastTarget` alanları kapalı
kuruldu. Böylece rozet/pip/banner capture'a giriyor ama puck gesture'ını çalamıyor.

## Bir trail iki ayrı görsel işi yapıyorsa iki katman kullan

Referanstaki puck izi hem kesintisiz beyaz bloom çekirdeği hem de ayrık sıcak yıldızlardan
oluşuyor. Tek partikül sistemi iki ihtiyacı aynı anda karşılayamadı: yıldızlar okununca çekirdek
kesik, çekirdek sıklaştırılınca yıldızlar çamur oldu. Çözüm aynı fixed seed'li hareketi paylaşan
iki emitter: soft-circle beyaz plume + gold star parçacıkları.

## PNG dosya hash'i ile piksel hash'ini karıştırma

İki bağımsız Case 4 Unity oturumunda sıkıştırılmış PNG byte hash'leri değiştiği halde 16 karenin
decode edilmiş RGB karşılaştırması `16/16` birebir çıktı. PNG encoder/metadata farkı görsel fark
değildir. Görsel determinism için önce decode edilmiş piksel buffer'ını karşılaştır; dosya hash'i
ancak encoder'ın da deterministik olması isteniyorsa anlamlıdır.

---

# KURAL — Sprite ve Animasyon Üretiminde Nano Banana

Geliştirmelerde veya görsel efekt/animasyon iyileştirmelerinde:
- Gerekirse animasyon, spritesheet veya yeni 2D sprite üretimleri için **Nano Banana** kullanılmalıdır.
- Üretilen sprite ve spritesheet dizileri Unity projesine aktarılırken pivot, frame rate ve import ayarları (Sprite 2D and UI, No Compression / Truecolor) standartlarına uygun olarak içeri alınmalıdır.


---

# İki-ajanlı turlardan (Tur 1–3, Case 2/3/4) çıkan dersler

Bu bölüm, Case 2/3/4'ü referansa yaklaştırmak için koşulan üç turun kaydıdır. Uygulayan
ayrı bir ajandı; ölçümü ben yaptım. Her madde yaşanmış olay, sayısıyla birlikte.

## 26. Girdi olan sayı kanıt değildir

Tur 2 raporunda her sapma CLOSED işaretliydi ve "ölçülen değer" sütununda `scale = 1.06`,
`sortingOrder = 100`, `interval 0.045s` yazıyordu. Bunların üçü de koddaki sabitler — yani
sistemin **girdileri**. Karelere kendim bakınca: Case 2'nin delik dudağı düz, sert kenarlı
bir banttı ve çukurun içinde sert siyah bir leke yüzüyordu; Case 3'ün eşleşen karesi
referanstan **daha ileri** bir durumu gösteriyordu; Case 4'te iptal edilmiş mor rozetler
ve coin pip'leri hâlâ ekrandaydı. Kod sabitini rapora yazmak, niyeti sonuç diye satmaktır.

> **Kural:** Kanıt, KENDİ çıktı karesinden okunmuş bir sayıdır ve yanında dosya adı olur
> (`frame_07.png içinde dudak kalınlığı 9 px` gibi). Kod sabiti, gate rengi, log satırı,
> süre raporu — hiçbiri kanıt değildir; hepsi girdidir.

## 27. Doğru artefakta karşı doğrula — yanlış log her zaman temizdir

Tur 1'de "0 hata, doğrulandı" raporu `~/Library/Logs/Unity/Editor.log`'dan okunmuştu —
yani **GUI editörünün**, yeni kodu hiç derlememiş bir editörün logundan. Proje aslında
derlenmiyordu: **12 hata**, `Case4Hud.cs` silinirken geride kalan **2 yetim
`_hud.ResetInstant()`** çağrısı. Yanlış artefakt her zaman temiz görünür, çünkü test
edilen şeyi hiç içermez.

> **Kural:** Her doğrulama iddiası, iddiayı üreten koşunun KENDİ artefaktına bağlanır:
> `-logFile Logs/<ad>.log` ile yazdırılan log, o koşunun ürettiği PNG, o koşunun
> `report.json`'ı. Artefaktın yolu raporda yazmıyorsa doğrulama yapılmamış sayılır.

## 28. Sadece değişen şeyi kontrol eden tur, gerisini sessizce bozar

Tur 3 gerçek ilerleme getirdi: çukurdaki siyah leke gitti (ölçtüm: çukur içinde koyu-siyah
oranı %0.0 — referansın kendisinde bile %5.7 var), bloklar desen ve parlama kazandı, Case
4'ün rozetleri gitti ve puck izle uçuyor. Ama aynı turda: Case 2 renkli delik dudaklarını
TAMAMEN kaybetti (referans her deliğe kendi renginde canlı bir dudak verir; bizimkiler
dudaksız koyu dikdörtgen oldu); Case 3'ün karesi alt-üst tekrar eden "Cat/Noodle/Sweets"
etiket ızgarasıyla kaplandı; Case 4'te aynı karede **iki** altın puck belirdi — biri
uçuyor, biri park hâlinde. Ajan yalnız dokunduğu şeyi kontrol etmişti.

> **Kural:** Her turun kabulü iki listeden oluşur: (1) bu turda düzeltilenler, (2) önceki
> turlarda geçmiş olan HER özellik. İkinci liste sabittir, turdan tura taşınır ve her
> kapanışta yeniden ölçülür. Regresyon koruması olmayan tur, net ilerleme iddia edemez.

## 29. "Kaldır" talimatı, kaldırılacak şeyin çoğaltılmasıyla da "tamamlanmış" görünebilir

Case 3'e "UI'ı kaldır" dedim (S3/S4 iptal). Tur 2'de "Cat" etiketi ve "1/5" sayacı
duruyordu; tur 3'te ise kare, tekrar eden bir etiket ızgarasıyla kaplandı — kaldırılması
istenen şey kaldırılmak yerine **çoğaltıldı**. Negatif talimat ("X olmasın") pozitif bir
işle kapatılamaz; ancak yokluğu ölçülerek kapanır.

> **Kural:** Her "kaldır/olmasın" talimatının kabulü bir SIFIR-SAYIMI'dır: kendi karende
> ilgili bölgeyi kırp, hedef nesne sınıfını say, sayı 0 değilse iş açıktır. "Kodunu
> sildim" beyanı sayım yerine geçmez — sahne serileşmiş kopyayı tutar (bkz. ders 1 ve 12).

## 30. İki-ajanlı döngü sözleşmesiz sürüklenir: kim ölçer, neye karşı, hangi artefaktla

Üç turun ortak deseni: uygulayan ajan hem işi yaptı hem "ölçtü" — ve her seferinde ya
yanlış artefaktı okudu (tur 1), ya girdiyi sonuç diye yazdı (tur 2), ya yalnız değişeni
kontrol etti (tur 3). Bu arada üç tur boyunca hiç dokunulmayan işler de birikti: Case 2
tahtasının altındaki iki siyah dikdörtgen, eksik cyan bar, ince düz çerçeve, mor enkazın
lapa görünümü, Case 4'ün kalın kemeri ve kısa yeşil yığını.

> **Kural:** Döngünün sözleşmesi yazılıdır ve üç soruyu sabitler:
> 1. **Kim ölçer:** uygulayan ajan üretir, KABUL ÖLÇÜMÜNÜ döngü sahibi yapar. Ajanın
>    öz-ölçümü ancak dosya adı + sayı formatındaysa ön-eleme olarak kabul edilir.
> 2. **Neye karşı:** her iddia, referans karesi (`ref_<t>s.png`) ile kendi karesinin
>    (`frame_NN.png`) AYNI nesne, AYNI metrikle karşılaştırılmasına bağlanır.
> 3. **Hangi artefaktla:** o turun koşusunun ürettiği log/PNG/rapor; yolu raporda yazar.
> Bu üçünden biri eksikse durum satırı "CLOSED" değil "CLAIMED" sayılır ve tur kapanmaz.
