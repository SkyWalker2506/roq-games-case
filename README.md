# ROQ Games — Game Feel Pass v2 (2026-08-19)

Bu klasor, kullanicinin mevcut ROQ Games case kodlari uzerinde yapilan **reference-feel odakli revizyonu** icerir.
Amac dort oyunu birebir kopyalamak degil; case brief'in istedigi kisa interaction'larda referanslarin **timing, easing,
object response, impact hierarchy, camera feedback ve polish** karakterini daha dogru yakalamaktir.

> Unity hedefi: **6000.3.11f1 / URP**. Bu paket tam starter Unity projesi degil; kullanicinin gonderdigi code bundle'in
> guncellenmis halidir. Ilgili klasorleri mevcut starter projenizde `Assets/` altindaki ayni klasorlerle degistirin.

## En onemli v2 prensibi

Onceki pass'te bircok interaction'da "daha fazla juice = daha iyi feel" gibi davranan bir enerji yigini vardi:
buyuk shockwave, yogun particle, guclu bloom, uzun trail, camera shake, hitstop ve overshoot ayni anda calisiyordu.
Referans kliplerde ise ana kalite daha cok sunlardan geliyor:

1. **Hareket cok cabuk okunuyor.** Input ile sonuc arasinda gereksiz bekleme yok.
2. **Obje kimligi kaybolmuyor.** Kirilan blok hala renkli blok parcasi; sticker hala kagit/sticker; Buca bloklari hala okunur blok.
3. **Impact tek bir ana vurgu.** Her alt faz ayri bir patlama degil.
4. **Secondary motion ana hareketi destekliyor, onunla yarisamiyor.**
5. **Kamera feedback'i kucuk.** Ekranin tamamini sallamak yerine temas anini belirginlestiriyor.

Bu nedenle v2 bir "daha cok efekt" pass'i degil; **enerji hiyerarsisi / restraint / timing pass'i**dir.

## Calistirma / sahneleri yeniden kurma

Kodlari starter projenizde `Assets/` altina aldiktan sonra Unity'nin compile isleminin bitmesini bekleyin ve Editor menulerini
sirayla calistirin. Bu adim kritik: sahnede daha once serialize edilmis eski tuning degerleri, yalnizca C# field default'u
degistirilince otomatik guncellenmez. Setup scriptleri component'leri yeniden kurup yeni degerleri sahneye yazar.

- `Case 1 > Build Fit The Shape Scene`
- `Case 2 > Build Block Hole Scene`
- `Case 3 > Build Stickerdom Scene`
- `Case 4 > Build Buca Scene`

Sonra `Assets/_Menu/Scenes/MainMenu.unity` sahnesinden her case'i Play Mode'da test edin.

## Input akislari

| Case | Input | Beklenen interaction |
|---|---|---|
| Fit The Shape | Sekle tap | Kisa direct transfer -> slot entry -> lokal wheel reaction |
| Block Hole | Blogu drag/drop | Eslesen delige snap -> renkli parcalanma -> hizli funnel/fall |
| Stickerdom | Sticker'a tap | Peel/page curl -> kisa transfer -> temiz attach/pop |
| Buca | Puck'i aim/release | Hizli ricochet -> green wall impact -> okunur block scatter -> hole drain |

## v2 timing ozeti

Asagidaki "once" degerleri kullanicinin gonderdigi kod bundle'indaki source tuning; "v2" yeni source tuning'dir.
Referans sureleri video karelerinden **yaklasik gorsel olcumdur**, oyun telemetry'si degildir.

| Case | Onceki source ritmi | v2 source ritmi | Degisiklik |
|---|---:|---:|---|
| Fit The Shape | ~1.37 s tam sekans | ~0.58 s tam sekans | hero arc/hover/VFX tail kisaldi |
| Block Hole | ~3.33 s scripted run; ~1.80 s drop tail | ~1.43 s scripted run; ~0.99 s drop tail | scripted wrong-hole detour kaldirildi; fall hizlandi |
| Stickerdom | ~1.65 s | ~0.92 s | peel korunup flight/flip/settle ciddi kisaldi |
| Buca | ~3.52 s (3-leg default) | ~2.18 s (3-leg default) | flight ~0.98 s; impact tail ~1.20 s |

## Case 1 — Fit The Shape

### Neyi duzelttik
- Flight path `OutBack` hero arc mantigindan **OutCubic / direct transfer** mantigina cekildi.
- `arcHeight 2.6 -> 0.42`, visible path overshoot kapatildi.
- Hover/approach `0.23 -> 0.035 s`; sekil slot ustunde gereksiz beklemiyor.
- Sink `0.14 -> 0.095 s`.
- Arrival shockwave ring **default OFF**.
- Target flash `3.2x -> 1.28x`; particle burst ve trail boyutu/lifetime ciddi azaltildi.
- Tum drum'a yayilan buyuk sparkle spill kaldirildi; reaksiyon target + iki yatay neighbour raporu + dusuk amplitudlu ripple ile sinirli.
- Camera punch / hitstop kucultuldu.
- Arka plan referanstaki acik lavender okunurluguna yaklastirildi.

### Hedef feel
Tap sonrasi sekil "gosterisli bir projectile" gibi degil, puzzle objesi gibi hizla hedefe gitmeli. Tatmin slot entry ve wheel'in
kisa reaction'inda olmali. Oyuncunun gozu VFX'i degil **shape -> matching slot** baglantisini okumali.

## Case 2 — Block Hole

### Neyi duzelttik
- Scripted capture'daki gereksiz **wrong-hole detour** kaldirildi; oyuncunun gercek yanlis drop davranisi yine calisiyor.
- Drop tail ~1.80 s'den ~0.99 s'e indi.
- Transparent / white-frosted "crystal" shard gorunumu terk edildi.
- Shard material eski projelerde transparent state serialize edilmis olsa bile `MakeOpaque()` ile zorla opaque'a cevriliyor.
- Parcalar block rengini koruyor (`whitening ~0.06`, alpha 1.0).
- Outward/rise kuvvetleri azaltildi; funnel/suction arttirildi. Sonuc: "beyaz bulut patlamasi" yerine **kiril -> deliğe dus** okunuyor.
- Debris/ring/dust VFX scale ciddi kucultuldu; ikinci close smoke kaldirildi.
- Hole glow daha dar ve daha az emissive.
- Camera shake/hitstop/punch azaltildi.

### Hedef feel
Asil beat: release -> lip contact -> block'un fiziksel kimligini koruyarak kirilmasi -> renkli chunk'larin hizla delige cekilmesi.
Efekt block'u ortmemeli.

## Case 3 — Stickerdom

### Neyi duzelttik
- Ekran kompozisyonu degisti: alttaki uc dev sticker "prototype tray" yerine **page icinde layered cluster** oldu.
- Ghost/target slotlar ustte daha kompakt bir row'a alindi.
- Kamera biraz daha yakin ve page odakli.
- Peel hala ana gorsel: 24 yerine 30 mesh segment, daha kontrollu curl/wave; white back surface daha okunur.
- `peel 0.30 -> 0.22 s`, flight `0.37 -> 0.30`, flip `0.30 -> 0.14`, settle `0.38 -> 0.11`.
- Flight arc `2.2 -> 0.58`; hareket sticker'i sahneden koparan bir projectile olmaktan cikarildi.
- Peel dust **OFF**, flight sparkle **OFF**, attach burst **OFF**, landing shockwave ring **OFF**.
- Attach aninda kucuk pop/scale reaction + ses birakildi.
- Camera punch cok azaltildi.

### Hedef feel
Sticker interaction'inin kalitesi particle'dan degil **paper curl, white backside, peel acceleration ve clean attach**'tan gelmeli.
Referanstaki en ayirt edici fiziksel sinyal budur.

## Case 4 — Buca

### Neyi duzelttik
- Arena rim artik idle'da quiet white/grey; **release aninda cyan aktive oluyor**. Onceki build ilk kareden itibaren neondu,
  bu nedenle launch beat'inin kontrasti yoktu.
- Flight 3-leg default ~1.34 s'den ~0.90 s'e indi; anticipation `0.20 -> 0.075`.
- Aim line inceltildi ve alpha azaltildi; max bounce 3 -> 2.
- Puck stretch `0.55 -> 0.16`; bounce squash ve elastic rebound azaltildi.
- Trail emission `120 -> 38`, trail scale `2.0 -> 0.72`.
- Bloom `1.05 -> 0.42`; threshold yukseltildi.
- Shake `0.46 -> 0.075`, punch `0.72 -> 0.13`, hitstop `0.09 -> 0.025`.
- Green wall artik tum duvar esit derecede konfetiye donusmuyor: fracture radius ciddi azaltildi; uzaktaki bloklar whole rigidbody
  olarak topple edebiliyor. Bu, referanstaki **okunur blok dagilmasi**na daha yakin.
- Coin count/scale/arc daha kontrollu; halen success accent ama debris'i kaplamiyor.
- Hole suction biraz guclendirildi, drain daha erken basliyor.

### Hedef feel
Buca'daki satisfaction buyuk ekran sarsintisindan degil; puck speed -> temiz contact -> fiziksel blok dagilmasi -> coin accent ->
hole'a akistan gelmeli.

## Degisen dosyalar

Tam liste `CHANGED_FILES.txt` ve satir bazli patch `PATCH.diff` icindedir. Ana runtime/editor degisiklikleri:

- Case1: `Case1Director.cs`, `ShapeArcFlight.cs`, `DrumSlotReaction.cs`, `Case1SceneSetup.cs`
- Case2: `Case2Director.cs`, `BlockShatterSink.cs`, `HoleGlowHighlight.cs`, `Case2SceneSetup.cs`
- Case3: `Case3Director.cs`, `StickerPeel.cs`, `StickerFlight.cs`, `Case3SceneSetup.cs`
- Case4: `Case4Director.cs`, `PuckLauncher.cs`, `PuckAimController.cs`, `GreenBlockShatter.cs`, `CoinArcStream.cs`, `Case4SceneSetup.cs`

## Dogrulama notu

Bu pass, gonderilen videolar uzerinden frame-strip karsilastirmasi ve source-level static review ile hazirlandi. Bu calisma ortaminda
Unity Editor executable'i bulunmadigi icin **Unity 6000.3.11f1 icinde compile/Play Mode render testi yapildigi iddia edilmiyor**.
Changed C# dosyalarinda delimiter/preprocessor yapisi, yeni method referanslari ve conflict marker kontrolleri statik olarak yapildi.
Final teslimden once yukaridaki dort Build menu'sunu calistirip 60 fps capture ile son micro-tuning yapin.

Ayrintili analiz, nedenler, riskler ve son test checklist'i icin `UPDATE_REPORT_TR.md` dosyasina bakin.
