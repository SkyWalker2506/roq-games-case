# Paketler arasi ortak dersler (P3 Case2'nin olculmus bulgulari)

Bunlar **olculdu**, tahmin degil. Her case paketi ayni tuzaklarla karsilasir.

## 1. `-executeMethod FrameStripCapture.Capture` CALISMAZ
Unity: *"has 1 arguments. Only methods with 0 arguments are supported."*
`Capture(string)` argumanli oldugu icin `-executeMethod` ile cagrilamaz.
**Cozum:** kendi case Editor script'inde **sifir-argumanli forwarder** yaz:
```csharp
public static void CaptureMyCase() => FrameStripCapture.Capture("SahneAdi");
```
ve gate'te onu cagir. (P3 `Case2SceneSetup.CaptureBlockHole()` ile boyle yapti.)

## 2. `Time.unscaledDeltaTime` TOPLAMAK ciddi drift uretir
P3'te 3.40 s'lik sekans **7.08 s** olarak olculdu — kareler gercek surenin iki katina yayildi,
yarisi sekans bittikten sonraya dustu. Sebep ~7000 fps'te delta birikim hatasi, yavas kare degil.
**Cozum:** her fazi **tek bir mutlak unscaled zaman cizelgesine** oturt
(`float t0 = Time.unscaledTime;` + `WaitUntil(() => Time.unscaledTime - t0 >= x)`),
delta toplama.

## 3. Sahnede **AudioListener YOK**
Staged sahnelerde AudioListener bulunmuyor — prosedurel seslerin hicbiri duyulmaz.
**Cozum:** setup script'inde Main Camera'ya `AudioListener` ekle (varsa ekleme).

## 4. Sahne-serilestirilmis alan degerleri C# initializer degisikliklerini YUTAR
Bir MonoBehaviour sahneye eklendikten sonra alan degerleri sahnede saklanir; kaynak koddaki
`= 0.35f` gibi initializer degisiklikleri **sessizce hicbir sey yapmaz**. Iki tuning turu bosa gitti.
**Cozum:** setup script'i komponentleri **yok edip yeniden ekle** — kaynak tek otorite olsun.
Boylece temiz bir klonda da ayni sonuc cikar.

## 5. `Prefabs/Fractured/FractureMeshes-Game/*.prefab` koku **devre disi** geliyor
Instantiate edince hicbir sey gorunmez. `SetActive(true)` gerekiyor.

## 6. Unity, VFX materyallerini ilk yuklemede yeniden serilestirebilir
URP eksik particle-shader property'lerini dolduruyor. Owns disindaysa `git checkout -- <dosya>`
ile geri al; fonksiyonel etkisi yok ama diff'i kirletir.

## 7. Batchmode'da gercek fare yok
Drag/tap kontrolcusu **hem gercek input hem programatik surus** kabul etmeli; yoksa kapi kosulamaz.

---

# Case 1'in olculmus ek dersleri (efekt gorunurlugu — en degerli kisim)

## 8. Efekt olcegini YANLIS metrikten turetme
Case1'de `CellSize` **delik** renderer bounds'undan turetilmisti: delik sig bir oyuk, 0.36 dunya birimi;
gercek hucre 1.33. Sonuc: flash, sparkle ve varista seklin boyutu **3-4 kat kucuk** cikti — rapor
yesildi ama ekranda neredeyse hicbir sey yoktu.
**Dogru metrik:** komsu nesneler arasi **pitch** (ornegin `Segment_c0_r0` ile `Segment_c1_r0` mesafesi).
Case2'nin "parcalar cok kucuk" sapmasi da ayni aileden.

## 9. Additive VFX'i nesnenin MERKEZINE dogurma — gorunmez olur
Hole/hucre bounds merkezi cismin **icinde** kalir; additive flash ve particle oradan dogunca
cismin kendisi tarafindan **depth-reject** edilir. Ates edildigi loga yazilir, ekranda hicbir sey olmaz.
**Cozum:** yuzeyin disina offset ver (AABB support along outward axis) ve VFX'i o noktada dogur.

## 10. Batchmode play session'in ILK karelerinde cok saniyelik stall var
Asset pipeline / shader derleme / editor indexleme yuzunden ilk karelerde 1-3 s'lik donma olur.
Katı bir mutlak zaman cizelgesi kullaniyorsan bu stall **ilk adima duser** ve geri kalan tum fazlar
no-op'a coker (olculdu: anticipation 2.74 s, ripple-settle 0.0002 s).
**Cozum:** `Start()`te warm-up kapisi — materyalleri gorunmez sekilde bir kez render et, VFX havuzunu
doldur, **ust uste 5 kare < 30 ms** olana kadar bekle (ust sinir ~1.7 s). Ayrica fazlari silen degil
**kaydiran** bir `Hold()` imleci kullan, ve sekans sirasinda >0.12 s suren her kare icin loga `STALL`
uyarisi bas — boylece bir daha sessizce basarisiz olamaz.

## 11. Staged sanat "oldugu gibi dogru" olmayabilir — BAK
Case1'de drum'in tum hucreleri mor `MysteryOverlay` altindaydi: ekranda **duz mor bir blok**
gorunuyordu, referansin parlak renkli hucre duvarinin tam tersi. Kapak hedef hucre disinda
kaldirilinca **en buyuk sadakat kazanci** bu tek degisiklikten geldi.
Ders: sahneyi acip **bak**; staged haliyle birakmak her zaman dogru degil.

## 12. SRP Batcher, MaterialPropertyBlock'u YUTAR
URP'de SRP Batcher acikken, `UnityPerMaterial` CBUFFER icindeki property'ler MPB ile override
edilemez — MPB degerleri sessizce uygulanmaz. Case3'te curl parametreleri hic gecmedi.
**Cozum:** custom shader'da bu property'leri CBUFFER **disinda** birak (shader SRP-Batcher-uyumsuz olur).
Tek/az renderer varsa batch kaybi onemsiz. Hazir URP shader'lari (ParticlesUnlit vb.) bu sorundan etkilenmez.

## 13. Easing secimi, 16 karelik seritte NEYIN YAKALANDIGINI belirler
16 kare 1.65 s'ye yayilinca peel fazina sadece ~3 kare duser. `InOutSine` ile "yari soyulmus"
okunabilir an iki kare ARASINA dusup hic yakalanmadi; `OutCubic` ile ayni faz tam kareye oturdu.
**Ders:** kisa bir fazin seritte gorunmesini istiyorsan easing'i ona gore sec — teknik dogru ama
karede gorunmeyen efekt, degerlendirme acisindan yok demektir.

## 14. Sahne hiyerarsisinde "root sandigin" nesne cocuk olabilir
`GhostSlots` scene root degil, `Page`'in cocugu — local y 3.45 ama **dunya y'si 7.45**.
Local pozisyonu dunya pozisyonu sanmak SETUP_FAILED uretti. Arama/eslestirmeyi derinlemesine yap.

## 15. Capture `rc=0` KISMI seriti de "basarili" sayar — mtime bakmadan sayi okuma

Case 3'te olculdu. `Case3SceneSetup.CaptureStickerdom` 16 karelik **kontak seridi** yolu;
240 karelik **yogun** yol `BatchCaptureRunner.CaptureDenseCase3` (once `SetFrameCount(240)`).
Yanlis olani calistirinca:

- `rc=0`, log'da `CAPTURE_DONE scenes=1`, sure 9 s — her sey basarili gorunuyor.
- Ama dizine sadece **16/240** kare yazildi; kalan **224 kare bir onceki kosudan kaldi**.
- Iki farkli zaman tabani karisti: ilk 16 kare 0.141 s araliklarla, geri kalani 0.009 s.
- `report.json` tazeydi ve `completed: true` diyordu — yani rapor da yalanlamiyordu.

**Cikis kodu bu durumu ayirt EDEMEZ.** Yakalanma sebebi tek sey oldu: kare dosyalarinin
`mtime`'ina bakmak (`frame_00` 02:53, `frame_150` 01:39).

**Kalici cozum (uygulandi):** `FrameStripCapture.StartNext` artik her sahnenin kendi
`OutputDirectory`'sindeki `frame_*.png` dosyalarini yeni serit yazilmadan once siliyor
(`ClearStaleFrames`, log satiri `CLEARED_STALE_FRAMES`). Boylece kisa kosu uzun kosunun
kuyrugunu birakamaz; bayat kare **var olamaz**. Bu, "her ajan mtime kontrol etmeyi
hatirlasin" disiplininden daha guclu bir garanti — silme sahne bazinda kapsamlanmis
oldugu icin paralel kosan diger case'lerin capturelarini etkilemez.

**Yine de kural:** bir capture'dan sayi okumadan once kare sayisini VE tazeligini dogrula.
Kismi serit, guvenli gorunen yanlis sayi uretir.

---

## `-quit`, `EditorApplication.update` suren her gate'i sessizce oldurur

Case 1 selection gate'i `-executeMethod Case1SelectionGate.SelectionGate -quit` ile kosuldu:

- `rc=0`, sure **12 s**, log'da `[Case1Gate] GATE_START entering play mode`.
- Ama **tek bir tap bile calismadi.** Log o satirdan sonra dogrudan
  `Batchmode quit successfully invoked - shutting down!` ile devam ediyor.

Sebep: gate `EditorApplication.EnterPlaymode()` cagirip isini `EditorApplication.update`
uzerinden yuruyor. `-quit`, `-executeMethod` doner donmez Unity'yi kapatiyor; update
donguse hic donmuyor. Gate'in kendi `Finish()` metodu zaten batchmode'da
`EditorApplication.Exit(exitCode)` cagiriyor — yani **`-quit` gereksiz ve zararli**.

Ayni kosu `-quit` olmadan: **22 s**, `SELECTION_GATE ... passed=5 failed=5`, 10 tap'in
hepsi calisti.

**Kural:** play mode'a giren ya da `EditorApplication.update` ile ilerleyen hicbir
`-executeMethod` cagrisina `-quit` ekleme. Sadece duz, senkron editor islerine ekle
(orn. `Case1SceneSetup.Build`). `rc=0` bu durumu ayirt EDEMEZ — bu, kismi serit
tuzagiyla ayni yanlis-basari ailesi.

## `-nographics` capture'i cokertir (exit 139)

`-batchmode -nographics ... -executeMethod Case1SceneSetup.BuildAndCapture` → **SIGSEGV**.
Stack'in dibi net:

```
FrameStripCapture:RenderToTexture (UnityEngine.Camera)
CameraScripting::Render(Camera*)
...
NoGraphicsMain()
```

`-nographics` grafik cihazi acmaz; `Camera.Render()` RenderTexture'a cizmeye calisinca
cokuyor. Kare yazilmiyor, dizin bos kaliyor.

**Kural:** capture kosularinda `-nographics` KULLANMA. Gate/build kosulari `-nographics`
ile sorunsuz (ve daha hizli); sadece kare ureten yollar grafik cihazi ister.

## 16. "Sub-3-px beraberlik" HARNESS ozelligi DEGIL — her metrik icin AYRI olcul

Case 3'te olculdu. Proje boyunca "3 px altindaki fark beraberliktir" kurali herkese
harness'in bir ozelligi gibi aktarildi. Olculmemisti.

**Olcum:** tek bir degismemis agac uzerinde 4 ardisik dense capture (240 kare):

| metrik | 4 kosu | yayilim |
|---|---|---|
| `totalDuration` | 1.7583312988281250 x4 | **0** |
| `trail_f150` | 1458 x4 | **0** |
| `burst_peak_net` | 1307 x4 | **0** |
| `elements` / `spread_w` / `half_frames` | 9 / 141 / 18 x4 | **0** |
| `blue_frac` | 0.9282 x4 | **0** |

Yani gercek beraberlik esigi **0**. "3 px" hem yanlis gerekcelendirilmis hem de
`burst_peak_net`, `elements`, `spread_w` gibi metrikler icin COK GEVSEK: orada 1 birimlik
degisiklik gercek sinyaldir, gurultu degil.

**Ama iki gercek oynaklik kaynagi var:**

1. **Adim sinirlari, recompile sonrasi ILK kosuda bir 120fps karesi kayar.**
   run1 `flight=0.35000 flip=0.11670`, run2-4 `flight=0.34170 flip=0.12500` — toplam korunuyor,
   sinir kayiyor. `totalDuration` da soguk kosuda 1.7583351135253906, isinmis kosularda
   1.7583312988281250. Adim suresine dayanan kontrol ya +-1 kare tolere etmeli ya da ilk
   kosuyu atmali. **Piksel ciktisi bundan etkilenmiyor** (yayilim yine 0).

2. **Capture'in ZAMANLAMA yoluna I/O koymak olcumu bozar.** `ClearStaleFrames` gecici olarak
   `StartNext` icine, `EnterPlaymode` hemen oncesine konuldugunda 240 senkron `File.Delete`
   olculen sureyi tam bir 120fps karesi kisaltti (1.75833 -> 1.75000), bu da 240 karenin
   ornekleme anlarini kaydirdi ve zamana duyarli her metrigi oynatti. `Begin()` icine
   (kuyruk kurulumu, oynatma yolundan uzak) tasiyinca sure 1.75833'e dondu ve yayilim 0 oldu.

**Kural:** bir sahnede sikko bir kontrole guvenmeden once determinizmi >=3 ayni kosuyla
OLC. Esigi metrik basina belirle. Bir onceki oturumdan gelen TEK bir ornegi taban olarak
kullanma. Ve capture'in zamanlama yoluna asla is ekleme.

## Absurd-value probe: baslangic degerini MATERYALDEN oku, shader default'undan DEGIL

Case 1'de `_CavityBounce`'un pixel'e ulasip ulasmadigini olcmek icin 12 yazildi ve socket
0-6 birim oynadi. "Demek ki property olu" diye yorumlandi. **Yanlisti.**

`Case1SceneSetup.cs` bu property'yi hic yazmiyor — bu dogru. Ama HEAD'deki materyal
asset'i zaten `_CavityBounce: 10.2` tasiyordu. Yani probe `1 -> 12` degil, **`10.2 -> 12`**
idi: %17'lik bir durtme. 0-6 birimlik oynama beklenen sonuc, olu property kaniti degil.

Daha kotusu, "negatifi dogruladim" adimi da yaniltti: `.mat` dosyasindan
`_CavityBounce: 12` okundu ve bu dogrulama sayildi. O okuma sadece **yazmanin
ulastigini** kanitlar; 12'nin baslangictan UZAK olup olmadigi hakkinda hicbir sey
soylemez.

**Kural:**
1. Bir property'yi probe etmeden once mevcut degerini **materyal asset'inden** oku
   (`grep _Prop Assets/.../X.mat`). Shader'daki `= 0` default'u baslangic degeri DEGILDIR —
   asset onu ezmis olabilir.
2. "Absurd" gorecelidir: baslangic 10.2 ise absurd deger 1 ya da 0'dir, 12 degil.
3. Negatifi dogrulamak = yazmanin ulastigini + baslangictan uzak oldugunu birlikte
   gostermek. Sadece birincisi dogrulama sayilmaz.

**Ilgili tuzak — materyal property'leri YAPISKAN:** `EnsureToonMaterial` sadece adini
verdigi property'leri yazar. `.cs`'i geri almak `.mat` asset'indeki ayarlanmis degerleri
TEMIZLEMEZ. Deney sonrasi `git checkout Assets/.../Materials/` sart; yoksa bir sonraki
olcum kirli baslangictan yapilir.

**Sahiplik karisikligi bu tuzagin kaynagi:** bazi degerler kodda (`_IndentFloorDarken`
her Build'de 0.72 yaziliyor), bazilari sadece asset'te (`_CavityBounce`, `_CavityLightKill`).
Asset'teki `_IndentFloorDarken: 0.79` her Build'de eziliyor — olu yazim. Bir property'yi
YA kod YA asset sahiplensin; ikisi karisinca baslangic degeri okunamaz hale gelir.

## Bir metrigin ILK negatifine inanmadan once POZITIF KONTROLU olmali

Case 1'de socket "taban duzlugu" (I5) metrigi iki tur boyunca 0.34-0.44 raporladi ve
"5/5 basarisiz" diye yazildi. Hedef 0.06 idi. Gercekte **taban zaten duzdu: 0.005-0.010**,
yani hedeften alti kat IYI. Metrik socket'in DUVARLARINI tabana dahil ediyordu; olctugu sey
duzluk degil, gecis kenarinin yuksekligiydi.

Maliyeti sadece yanlis bir satir degildi: var olmayan bir kusur pesinde tur harcandi ve
asagi akistaki her karar bu sayinin dogru oldugu varsayimina kosulluydu. Downstream'de ne
kadar dikkatli olunursa olunsun yakalanamazdi — cunku her sey o sayiya bagliydi.

**Kural:** bir metrigi ilk kez yazdiginda, ona **gecmesi gerektigini BILDIGIN** bir girdi
ver ve gectigini gor. Pozitif kontrolu olmayan bir metrigin ilk negatifi kanit degildir.
Uygun pozitif kontroller:
- referans karesinin kendisi (hedefi o tanimliyorsa gecmeli),
- kasten kusursuz uretilmis sentetik bir ornek,
- ayni sahnenin metrigin umursamadigi bir bolgesi.

Bu, "probe'un baslangic degerini materyalden oku" kuralinin **ebeveyni**: orada olcumun
BASLANGICI dogrulanmamisti, burada olcumun KENDISI. Ikisi de ayni soruyu sormakla onlenir:
*bu sayi, dogru oldugunu bildigim bir durumda ne veriyor?*

---

# Baska bir baglamdan devralinan tolerans, sayi kilifina girmis bir tahmindir

Bu, bu dosyadaki en genel kural — ve bu oturumda dordu ayri ajan tarafindan ihlal edildi.

"3 px altindaki fark beraberliktir" esigi bu projede dort ajana, hicbiri olcmeden aktarildi.
Bir sayi oldugu icin olculmus gibi gorundu; oysa baska bir isin baska bir metriginden
gelmisti. Yanlis yonde yanlisti: COK GEVSEKTI, yani gercek sinyali gurultu diye eledi.

Bir toleransin nereden geldigi, ne kadar dogru oldugundan daha onemlidir:

- **Bu metrik icin, bu sahnede olculdu** → kullanilabilir.
- **Baska bir metrik, baska bir sahne, baska bir oturum** → sayi degil, tahmin. Yeniden olc.
- **Kaynagi bilinmiyor** → en tehlikelisi. Devralinan bir esik, yazili bir kaynagi yoksa
  kullanilmaz; yeniden turetilir.

Toleransi devralmak yerine gurultu tabanini olcmek genelde tek bir fazladan kosuya mal olur.
Bu oturumda o kosu yapildi ve taban **tam olarak sifir** cikti (asagi bak) — yani devralinan
+-3 esigi gercek sinyalin 3 birimine kadarini gormezden gelecekti.

**Kural:** bir rapora esik yazarken yanina nereden geldigini yaz. Kaynagi yazamiyorsan
o esigi kullanma.

---

# Case 3 sRGB/lineer ve serit gecisinden olculmus ek dersler (2026-08-25)

## Dense capture'in giris noktasi `BuildAndCapture` DEGIL

`Case3SceneSetup.BuildAndCapture` -> `FrameStripCapture.Capture` ile **16 karelik**
serit uretir ve `rc=0` doner. Metrik betikleri 240 kare bekler.

**Dogru cagri:**
```
tools/unity-run.sh -batchmode \
  -executeMethod BatchCaptureRunner.CaptureDenseCase3 \
  -logFile .plan-build/logs/<ad>.log
```
`BuildAndCapture` ile kosulursa betikler "partial/stale strip" der; bu bir hata degil,
yanlis giris noktasidir. Ayni sey Case 2 (254) ve Case 4 (340) icin de gecerli.

## Gurultu tabani: DURAGAN kareler bit-birebir, ANIMASYONLU kareler degil

Degismemis agac uzerinde iki bagimsiz dense capture karsilastirildi (240 kare, piksel piksel):

| bolge | fark |
|---|---|
| kare 0-88 (idle) | **0** — dosyalar bit-birebir ayni |
| kare 239 (settled) | **0** |
| kare 89-239 (animasyonlu) | 66 kare farkli, en buyuk kanal farki 174 |
| kare basina ortalama L farki, 89-239 | en fazla 0.0071 |

Yani bir sayfa/gorunum degisikligini **idle karede** olc: orada taban tam sifirdir ve
1 kodluk fark bile gercek sinyaldir. Animasyonlu karede olcersen tabanini ayrica belirlemen
gerekir.

## Kivilcim (sparkle) yerlesimi, sahneden nesne silinince yeniden dagilir

Option C uc `GameObject` sildi. Ucus, secilen sticker, inis slotu, `totalDuration` ve adim
sinirlari birebir ayni kaldi; ama `carry_f177` 227 -> 142, `elements` 9 -> 14 oynadi.
Sebep patlama efektinin bozulmasi degil: kivilcim parcaciklarinin **yerlesimi** degisti
(f170 kirpmalarinda cikplak gozle gorulur — inen kedi ayni yerde, kivilcimlar baska yerde).
Ayni sahnede tekrarli kosular ayni sonucu verir; sahne hiyerarsisi degisince desen kayar.

**Kural:** parcacik sayan bir metrigin sahne cerrahisinden sonra oynamasi, efektin
bozuldugu anlamina GELMEZ. Once ayni karede efektin kendisine bak.

## Sayfa genelindeki maskeler sayfa icerigini de sayar — idle karede ayristir

`trail_f150` (sari-limon maskesi, tum kare) sayfa duzenlemesinden sonra 1466 -> 1264 dustu.
"Iz kucildi" gibi okunur. Ayni maske **iz iceremeyen** idle kare 60'ta kendi basina
636 -> 239 (-397) dusuyordu: dususun tamami sayfadan kalkan sari cizimlerdi, izden degil.

**Kural:** tum-kare bir maske metrigi oynadiginda, olcmek istedigin olayin OLMADIGI bir
karede ayni maskeyi kosr. Fark orada da varsa, metrik senin degistirdigin seyi olcmuyordur.

## Bir esigin BIRIMINI yaz — Linear projede sRGB sayisi sessizce olu kontrol uretir

`PageObjectDim.shader`'da dort esik sRGB sekilli sayilardi ama **lineer** luminansla
karsilastiriliyordu (`m_ActiveColorSpace: 1`). Sonuc iki farkli turde hataydi:

- `_Lo0/_Lo1` (0.18/0.34): `smoothstep` sRGB 0..128 araliginda tam olarak 0 dondu, yani
  `_Darken` o aralikta **hicbir sey yapmadi**. Shader'in kendi aritmetigi calistirildiginda
  40..120 girdileri bit-birebir ayni cikti. Ayarlanmis, rapor edilmis, hicbir seye bagli
  olmayan bir kontrol.
- `_Hi0/_Hi1` (0.5/0.9): olu degil, **yanlis yere nisanli**. Highlight rolloff'u
  sRGB 188..243'te kosuyordu, olmasi gereken 127..229 yerine.

Ikisi ayni kok sebepten cikar ama ayni sey degildir ve raporda ayri anlatilmalari gerekir:
biri "kontrol yok", digeri "kontrol yanlis yerde".

**Kural:** bir esik property'sine yorum olarak BIRIMINI yaz (lineer mi sRGB mi). Linear
renk uzayindaki bir projede sRGB sezgisiyle yazilmis her esik sessizce yanlistir ve
hicbir derleme hatasi vermez.

## Ayni sayi iki yerde yasar: shader default'u VE `.mat` asset'i

Dort esik hem `PageObjectDim.shader` icinde hem de on bir
`Materials/Case3_PageObjectDim_*.mat` dosyasinda serilesmisti. Sadece shader'i duzeltmek
hicbir sey degistirmez — materyal serilesmis degeri ezer. `.mat` dosyalari ayrica
`_Darken`'i shader default'u 0.84 yerine 0.94 tasiyordu; aritmetigi shader default'uyla
modelleyen bir analiz yanlis cikardi.

**Kural:** bir shader property'sini degistirmeden once
`grep -l "_Prop" Assets/**/Materials/*.mat` ile kac kopyasi oldugunu say.
(Bu, yukaridaki "absurd-value probe" kuralinin ayni ailesi.)

## `Shadow_*` nesneleri `Sticker_*`'in COCUGUDUR

`Stickerdom.unity`'de her golge kendi sticker'inin cocugu. Ebeveyni silen golgeyi de siler.
Silme listesine ikisini birden yazan bir toplu islem `applied 3/6` gorur ve sahneyi
kaydetmeden basarisiz olur. Ebeveyni sil, cocugun gittigini **dogrula**, varsaydigin
yerde assert et.

## Tum invariant'lar gecerken kullanici hala goremiyorsa: OLCEK'i olcmediniz

Case 1'de socket'in bes invariant'i da referans bandindaydi - taban karanligi, kenar
rampasi, pah halkasi, renk, taban duzlugu - ve sahibi yine "gocuk daha belirgin olsun"
dedi. Hepsi **socket'in ICINDE** olculen oranlardi. Hicbiri socket'in **hucresinin ne
kadarini kapladigini** olcmuyordu.

Olculdugunde: bizimkiler hucre genisliginin %24-39'u, referans %29-53'u. Yildiz
referansin yarisindan kucuktu. Kusursuz bicimlenmis ama yari boyda bir socket bes
testten de gecer ve normal izleme mesafesinden delik gibi okunmaz.

**Kural:** bir gorunumu oranlarla (intensive) tarif ederken, en az bir **olcek**
(extensive) olcusu de bulundur: nesnenin kapsayicisinin ne kadarini doldurdugu. Oranlar
"dogru bicimde mi" sorusunu yanitlar, olcek "gorulecek kadar buyuk mu" sorusunu. Kullanici
ikincisini sorar.

**Ayni turdan ikinci ders — iki metrik celisirse GOZ hakem olsun.** Piksel sayan bir
"socket alani / hucre alani" metrigi bizimkilerin referanstan BUYUK oldugunu soyledi
(0.73x), tarama-cizgisi metrigi ise KUCUK (1.37x). Bir referans hucresi ile bizimkini
ayni hucre genisligine olceklendirip yan yana koyup BAKMAK kavgayi bitirdi: bizimki
gozle gorulur sekilde kucuktu. Alan metriginin yuz-satiri tespiti bozuktu (referans
ucgenin yuzunu 12 satir, elmasinkini 84 satir buluyordu). Celisen iki sayidan hosuna
gideni secme; ikisini de goruntuye karsi sina.

---

## 17. Bir olcum, makine baska bir sey yaparken alinirsa O BASKA SEYI olcer

Play mode'a girisin neden yavas oldugu arastirilirken olculdu. Unity GUI'de acilip
hemen play mode'a sokuldu ve sonuc **11.129 ms** cikti; bunun **8.865 ms**'si
`EnteredPlayMode` ile 5. kare arasindaydi. Uc ardisik kosuda tekrarlandi
(11.1 s / 10.6 s / 10.2 s), yani gurultu degildi. Neredeyse "ilk karelerde saniyelerce
suren bir stall var" diye raporlanacakti.

Yanlisti. O 8,8 saniye play mode'un maliyeti degildi: Editor hala **acilis isini**
yapiyordu ve play session onunla ayni CPU'yu paylasiyordu. Log'da acikca duruyordu:

```
Asset Pipeline Refresh ... ImportOutOfDateAssets: 870ms ... Untracked: 413ms
Start Indexing on Editor startup
[Indexing] Starting Initial Indexing for Assets
Starting: .../bee_backend ... ScriptAssemblies
```

Olcume tek bir sey eklendi — play mode'a girmeden once Editor'un **bosalmasini** bekle
(`EditorApplication.isCompiling == false && isUpdating == false`, uzerine 60-90 s pay):

| | frame 5'e kadar | EnteredPlayMode -> frame 5 |
|---|---|---|
| acilisla yarisan olcum | 10.159 - 11.129 ms | 7.928 - 8.865 ms |
| bosalmis Editor | **2.905 ms** | **175 ms** |

Ayni agac, ayni sahne, ayni harness. Fark tamamen olcum kosulundaydi — ve 8,8 saniyelik
"bulgu" tamamen kayboldu.

**Kural:** bir sureyi olcerken makinenin baska is yapmadigini ONCE dogrula. Unity'de bu
somut olarak sudur:

1. Batchmode'da `-executeMethod` cagrisi asset pipeline refresh'i ve ilk import'u
   **beklemez**; olcum onlarla ic ice girer.
2. GUI'de CLI'dan yeni acilan bir Editor en az 30-60 s boyunca import, indexleme ve
   ScriptAssemblies derlemesi yapar.
3. Kullanicinin sikayet ettigi sey **isinmis** bir oturumdur. Soguk acilisi olcup
   "play mode yavas" demek, sorunun kendisini degil acilisi raporlamaktir.

Bu, "kismi serit" (ders 15) ve "`-quit` gate'i oldurur" tuzaklariyla ayni aileden:
sayi uretilir, makul gorunur, ve **yanlis seyi** olcer. Cikis kodu da, sayinin kendisi de
bunu ayirt edemez — sadece kosulu degistirip tekrar olcmek ayirt eder.

### Bu olcumden cikan ikinci sonuc: tahmini once ranked tablo ile sina

Ayni arastirmada hipotez "12.525 satirlik Editor kodundaki on `[InitializeOnLoad]` sinifi
her domain reload'da para odetiyor" idi. Her static ctor **tek tek olculdu** (Unity'nin
daha erken isledigi bir assembly'den `RuntimeHelpers.RunClassConstructor` ile zorlanarak):

| | ms |
|---|---|
| on static ctor'un TAMAMI | **1,9** |
| Unity'nin kendi `ProcessInitializeOnLoadAttributes`'i (paketler) | 335 |
| play mode'a giris toplami | 2.375 |

Yani projedeki tum editor kodu silinseydi **%0,08** kazanilacakti. Gercek maliyet
reload'larin kendisiydi (`m_EnterPlayModeOptions: 0 -> 3` ile 2.375 ms -> 564 ms,
tekrar Play'lerde ~2.000 ms -> ~100 ms).

**Kural:** "su kod yavaslatiyor" hipotezi, o kodu silmeden once **kalem kalem olculmus
bir tablo** ile sinanir. Tablo cevabi "hicbiri" olsa bile teslim edilecek urundur —
bu projede iki ayri makul hipotezi olcum oldurdu.

---

# Case 1 socket turundan olculmus dersler (2026-08-25)

## 18. `Case1SceneSetup.BuildAndCapture` **BUILD ETMEZ** — sadece capture eder

Govdesi tek satir: `CaptureFitTheShape();`. `Build()` cagrilmaz. Yani `EnsureToonMaterial`
kosmaz ve `.cs` icindeki her `m.SetFloat(...)` degisikligi **serite hic ulasmaz**;
capture, `.mat` asset'lerinde duran ESKI degerlerle render eder.

Bu tek kosuda dogal bir deney olarak goruldu. Ayni kosuda iki degisiklik vardi:

| degisiklik | nerede | serite ulasti mi |
|---|---|---|
| yildiz SDF'inin `rf` degeri 0.48 -> 0.62 | `SoftPlastic.shader` | **EVET** (socket alani 0.3164 -> 0.3780) |
| `_IndentScale` yildiz 1.82 -> 1.02 | `Case1SceneSetup.cs` | **HAYIR** (span 0.820, kilinda degismedi) |

Shader kaynagi degisince Unity onu yeniden derler; materyal property'si ise ancak
`Build()` onu asset'e yazarsa degisir. `rc=0`, 16 taze kare ve `completed: true` bu
farki **ayirt edemez** — "kismi serit" ve "`-quit` gate'i oldurur" ile ayni
yanlis-basari ailesi.

**Kural:** `.cs` icinde bir materyal property'si degistirdiysen once
`Case1SceneSetup.Build`, sonra `BuildAndCapture`. Kosudan sonra
`grep _Prop Assets/.../X.mat` ile degerin asset'e **gercekten yazildigini** dogrula.

## 19. `Case1SceneSetup.Build`, `-quit` OLMADAN isini bitirip sonsuza kadar oturur

DERSLER'in `-quit` maddesi dogru kurali soyluyor ("sadece duz, senkron editor islerine
ekle, orn. `Case1SceneSetup.Build`") ama ters yonu yazmiyor: **duz senkron bir editor
isine `-quit` vermezsen Unity cikmaz.**

Gorunen tablo tam bir hang taklidi: log `TrimDiskCacheJob: Current cache size 0mb`
satirinda duruyor, hicbir yeni satir gelmiyor, islem sabit ~21% CPU yakiyor. 30 dakika
boyle beklendi, sonra oldurulup iki kez daha denendi. **Uc kosunun ucu de isini
bitirmisti** — bu ancak `git status` materyalleri "modified" gosterdiginde fark edildi.
`Build()`'in kendi `Debug.Log`'lari da log'da duruyordu (`grep -c "Case1Setup"` = 32),
yani "log durdu" gozlemi bastan yanlis okunmustu: log durmamisti, is bitmisti.

Maliyeti: ~50 dakika ve uc `kill -9`. Kill'lerin kendisi de zarar verdi — ilki asset
import'unu yarida kesti ve sonraki kosuya bir shader yeniden derleme turu odetti,
bu da "demek ki gercekten yavas" yanlis teshisini besledi.

**Kural:** `-quit`'i "gate'lerde tehlikeli" diye hatirlama; **hangi is icin gerekli
oldugunu** hatirla. Play mode'a giren / `EditorApplication.update` suren is: `-quit` YOK
(kendi `Exit`'i var). Duz senkron editor isi: `-quit` ZORUNLU. Ve bir kosuyu "asilmis"
ilan etmeden once `git status` ile **urunune** bak, log'un son satirina degil.

## 20. Merkezden gecen bir TARAMA CIZGISI, uclari merkez disinda olan sekli az olcer

Case 1'de yildiz socket'i "referansin %51.2'sine karsi %49.5" olarak raporlanmisti ve
sahibi yine "yildiz tasmis" dedi. Celiski metrikteydi: olcu, socket'in merkezinden gecen
bir tarama cizgisiydi. Bes uclu bir yildizin merkezinden gecen yatay cizgi **belini**
keser, uclarini degil — yani tasan miktarin tam olarak kendisini gormez. Yildiz, bu
eksik okumayi telafi etmek icin BUYUTULMUSTU (`_IndentScale` 1.82).

Sinirlayici kutu + kapsayiciya olan **acikliK** ile yeniden olculdugunde:

| | spanX | spanY | hucre kenarina min aciklik |
|---|---|---|---|
| referans yildiz | 0.458 | 0.438 | 0.125 |
| bizimki (once) | **0.820** | **0.737** | **0.000** |

0.000 mecazi degil: socket maskesi hucrenin ust kenarina **degiyordu**. Bagimsiz
dogrulama bedava geldi — socket kenara degdigi surece yuze uygulanan flood fill
socket'i kapali bir delik olarak kapatamiyor, degmeyi birakinca kapatiyor.

**Kural:** bir seklin kapsayicisini ne kadar doldurdugunu olcerken tarama cizgisi
kullanma; sinirlayici kutuyu ve kenara olan acikligi olc. Tarama cizgisi sadece
uclari merkezden gecen ekseni ustunde olan sekiller icin dogrudur (kare, altigen);
yildiz, ucgen ve yildiz benzeri her sekil icin sistematik olarak az okur.

## 21. "Karanlik" derinlik degildir — DUVAR derinliktir

Sahibi "derinligi artir" dedigi anda socket'in her **oransal** derinlik ipucu zaten
referansi kariyordu: taban/yuz luminans orani 0.068-0.178'e karsi referans 0.067-0.152,
socket ici kontrast 0.54-0.85'e karsi 0.51-0.76, ust-alt gradyani +0.42..+0.77'ye karsi
+0.16..+0.33. Bizimki referanstan **daha koyu ve daha gradyanli**ydi ve yine duz
okunuyordu. Bu yuzden "daha koyu yap" refleksi yanlis yondu.

Ayirt eden sey, socket kenarindan iceri dogru alinan luminans profilinin **sekli**:

- referans: 4.16 / 1.86 / 1.45 / 1.21 / 1.06 / 0.95 / 0.90 / 0.88 / **0.87** / 0.87 /
  0.88 / 0.91 / 0.96 → bir duvar boyunca iner, iceride bir **cukura** oturur, sonra
  taban merkezine dogru %13-26 geri toplanir.
- bizimki: 6.09 / 3.04 / **0.89 / 0.89 / 0.90 / 0.90 / 0.90 ...** → 3. halkada tabana
  carpiyor ve olu duz devam ediyor. Geri toplanma %0.5-10.8.

Ve kritik ayrinti: bizimkinde duvar genisligi **her hucrede tam olarak 3 px**'ti. Bir
sabit, seklin bir ozelligi degil — cunku iceri dogru duvar bandi
`0.8 * _IndentBevel` idi ve hepsi ayni bevel'i paylasiyordu. Metrigin ayni sayiyi bes
farkli sekilde vermesi, oradaki seyin geometri degil **sabit** oldugunun isaretiydi.

Tabani ve kenari olup arasinda duvari olmayan bir oyuk, konturlu duz bir plakadir.

**Kural:** bir gomulme/kabartma ipucunu olcerken en az bir kez **kenardan iceri profil**
cikar. Ortalama koyuluk, kontrast ve gradyan gibi toplu istatistikler duvarin varligina
KOR — duvarsiz duz taban bunlarin ucunde de gecer, hatta referansi asabilir.

## 22. Bir bayrak, proje KLASOR adiyla eslesiyor olabilir

`Case1SceneSetup.EnsureToonMaterial`'da:

```csharp
bool isPiece = path.Contains("Case1_Playable_") || path.Contains("Piece")
            || path.Contains("Shape") || path.Contains("Tray");
bool isCell  = !glyph && !plate && !isPiece;
```

Her materyal yolu `Assets/Case1_FitTheShape/Materials/...` altinda. `"FitTheShape"`
icinde `"Shape"` var. Yani `isPiece` bu fonksiyonun gordugu **her** materyal icin true
ve `isCell` **hicbir zaman** true degil: yedi ayri shading degerinin hucre dali
(`_Smoothness`, `_SpecularStrength`, `_ShadeStrength`, `_BevelDarken`, `_RimLift`,
`_VertShade`, `_BottomDarken`) olu kod. Derleme uyarisi yok, calisma hatasi yok.

Yakalanma sekli: yeni bir property `isCell ? 0.070f : 0f` ile yazildi ve asset'e
**0** dustu. Property'nin asset'teki degerini okumak (kural: "baslangici materyalden
oku") bayragi da acige cikardi.

**Kural:** yola bakarak siniflandiran bir bayrak yazarken, aradigin alt dizginin proje
adi / klasor adi / sahne adi icinde de gecip gecmedigini kontrol et. Somut kanit iste:
her dal icin en az bir materyalde beklenen degeri `grep` ile dogrula.

## 23. Bir sekil ailesini tek bir olcekle normalize etmeye calisma

`EvaluateShapeSDF`'in ilkelleri birbirine gore normalize DEGIL: kare `0.56`, ucgen
`0.72`, altigen `0.58`, yildiz `0.78` yariciapla yaziliyor ve yildizinki **ucun**
yaricapi. Ayni `_IndentScale` bu yuzden yildizi kareden hatiri sayilir buyuk yapar.
Sekil basina olcek tablosu dogru cozum ama tablonun kendisi de **sekle uygun bir
metrikle** cozulmeli — yoksa (bkz. ders 20) tablo yanlis sayiyi telafi eder.

---

# Bir predicate'i TUM YOLA karsi test etme — proje adi o kelimeyi zaten iceriyor olabilir

Case 1'de olculdu. `Case1SceneSetup.EnsureToonMaterial` malzemeyi soyle siniflandiriyordu:

```csharp
bool isPiece = path.Contains("Case1_Playable_") || path.Contains("Piece")
            || path.Contains("Shape") || path.Contains("Tray");
bool isCell  = !glyph && !plate && !isPiece;
```

Bu metoda gelen HER yol `Assets/Case1_FitTheShape/Materials` ile basliyor — ve
`Case1_FitThe**Shape**` icinde "Shape" gecıyor. Yani `isPiece` bu metodun bugune kadar
yazdigi **her** malzeme icin true'ydu; `glyph`, `plate` ve `isCell` dallari **bir kez bile
calismadi**. Hucreler, tepsi parcalarina ait parlaklikla render edildi
(`_SpecularStrength` 0.38, `_RimLift` 0.10 — hucre icin yazilmis 0.24 / 0.05 yerine).

**Nasil yakalandi:** kodu okuyarak degil, **sevk edilmis asset'lerden**. `Case1_DeckSlotPlate.mat`
(adinda "Plate" var, `plate` dalina gitmeliydi) ve `Case1_CellCover.mat` (bir hucre) ikisi de
isPiece setini birebir tasiyordu: 0.72 / 0.38 / 0.10 / 0.45 / 0.18 / 0.15 / 0.58 / 0.45 —
28 `Case1_Toon_*` hucre malzemesiyle ayni. Uc farkli sinifin ayni sekiz sayiyi tasimasi
tesaduf olamaz.

**Neden onemliydi:** `_SpecularStrength` shader'in iki EKLEMELI teriminden biri. Spekuler
lob tam **canli sirada** dusuyordu, yani oyuncunun baktigi tek sirada. Bes renk ailesi
uzerinden `render_linear = s * base_linear + c` uydurmasi canli sirada
c = +0.086 / +0.041 / +0.068, bir alt sirada (ayni malzemeler, ayni shader)
c = -0.007 / -0.004 / -0.003 verdi. Doymus bir rengin altina konan notr bir taban tam olarak
"solmus" demektir — sahibinin sikayeti buydu.

Bu, **`PageObjectDim`'in lineer luminansa karsi sRGB esikleri** ve **puck'i tutturan
malzeme-adi aramasi** ile ayni ailedendir: apacik dogru gorunen, derleyicinin sikayet
etmedigi ve var olma sebebi olan dali **hic bir zaman** almamis bir predicate.

**Kural:**
1. Bir yol uzerinde `Contains` ile siniflandirma yapiyorsan **dosya adina** bak
   (`Path.GetFileName`), tum yola degil. Depo/proje/klasor adlari senin anahtar
   kelimelerini icerebilir ve bunu kimse fark etmez.
2. Bir predicate'in dogru oldugunu **ciktisindan** dogrula: her dalin gercekten
   ureteceği degerleri sevk edilmis asset'lerde ara. Iki farkli sinif ayni degerleri
   tasiyorsa dallardan biri olu demektir.
3. **Hic calismamis bir deger olculmus bir deger DEGILDIR.** Predicate duzeltilince
   `isCell`'in 0.24'u ortaya cikti ve 0.38'den daha iyi olmadigi olculdu: `_Smoothness`
   de 0.72 -> 0.55 dusuyor, bu da lobu (`lerp(16,128,s)`: 96.6 -> 77.6) gucun dustugu
   carpanin neredeyse aynisi kadar genisletiyor. Uzun suredir kodda duran bir sayiyi,
   sirf yazilmis oldugu icin, dogrulanmis gibi kabul etme.

## Ilgili: DUZ bir yuzeyi "en dusuk varyansli pencere" ile ornekleme YANLIS yuzeyi bulur

Ayni oturumda bir sayiyi yanlis raporlamama sebep oldu. Hucre yuzunden `_BaseColor`'a giden
zincirin "kanal basina 0.693 / 0.617 / 0.373, yani skaler degil" oldugu raporlandi. Yanlisti.

Hucre icinde en dusuk varyansli 11x11 pencereyi arayan orneleyici, renkli karelerde hucrenin
**alt pah bandina**, notr karelerde ise **isikli yuze** oturuyordu — iki farkli yuzeyi
karsilastiriyordu. Yuz gercekten duz oldugu icin (referansta ornek std'si 0.00) dogru
orneleyici, hucrenin **en parlak kumesinin MODU**: konumdan bagimsiz. Onunla olculunce zincir
notr ve gamma uzayinda neredeyse birim cikti (1.000 -> 254, 0.800 -> 206, 0.600 -> 152,
0.400 -> 98, 0.200 -> 46; her adimda kanal yayilimi 0.0).

**Kural:** duz bir bolgeyi ararken "en duz pencere" degil, **bilinen bolgenin modunu** al.
Bir sahnede birden fazla duz yuzey vardir ve varyans hangisinin dogrusu oldugunu bilmez.

## Ilgili: notr bir rampa, doymus bir rengin KUCUK kanallarini kalibre EDEMEZ

Notr kalibrasyon "taban 51 -> 46" diyor. Ama ayni kanal, diger iki kanal parlakken
**51 -> 28** render ediliyor. Notr rampada parlak bir pikselin icinde karanlik bir kanal
hic bulunmadigi icin bu etkiyi gosteremez. Cozum: kalibrasyonun ilk adimini notr rampayla
at, sonra **yerinde (in-situ)** olcumle — gonderilen taban ve gercekte ne render ettigi —
aile ve kanal basina ikinci bir adim at.
