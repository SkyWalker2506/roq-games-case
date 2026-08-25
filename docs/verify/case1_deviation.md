# Case 1 — Fit The Shape: referanstan sapmalar

Olculen sekans (`report.json`, replay pass): `completed=true`, toplam **1.387 s**
anticipation 0.106 · flight 0.481 · entry-sparkle 0.570 · ripple-settle 0.230
**ucus+giris = 1.051 s** (kabul bandi 1.0-1.6) ✅ · 7 farkli JuiceEvent ✅
Log kaniti: `sparkle spilled onto 3 neighbouring cells` · `ripple staggered across 75 cells, longest delay 0.420 s`

## Duzeltilecek sapmalar (oncelik sirasi)
| # | Sapma | Referansta | Bizde | Oncelik |
|---|---|---|---|---|
| 1 | **Arka plan rengi** | doygun **mor** — sahneye canlilik ve kontrast veriyor | **bej/krem** duz zemin; tum kare soluk okunuyor | **cok yuksek** |
| 2 | **Slot seridi yok** | drum'in ortasinda **beyaz cerceveli yatay serit** (hedef slot satiri), sol/sag ucta acik mavi ok tutuculari | serit hic gorunmuyor; hedef hucre sadece renkle ayirt ediliyor | **cok yuksek** |
| 3 | Hedef hucre vurgusu | hedef hucre parlak ve **cerceve icinde** — bakis oraya kilitleniyor | sari hucre var ama cevresinden yeterince ayrilmiyor | yuksek |
| 4 | Drum doygunlugu | hucre renkleri parlak ve yuksek kontrast | renkler daha koyu/soluk | orta |
| 5 | Sparkle tonu | biraz daha **beyaz ve ince** | biraz daha doygun sari | dusuk |

**2 numara icin ipucu:** repoda `Models/SLOT-TUTUCU.fbx`, `SHAPE-SLOT-PART.fbx`, `SM_SHAPE_SLOT.fbx`
ve sahnede `SLOTINSIDELIGHT` var — serit varliklari mevcut, sahnede gorunur hale getirilmeli.

## Sapma OLMAYAN, dogru calisan
- Drum'in mor `MysteryOverlay` kapagi kaldirilmis -> referanstaki **renkli hucre duvari** okunuyor
  (ajanin raporuna gore en buyuk sadakat kazanci bu tek degisiklikten geldi)
- Sekil kavisli yayla ucuyor, buyuyor, hucreye giriyor
- Sparkle **komsu 3 hucreye tasiyor**; ripple 75 hucreye merkeze uzakliga gore gecikmeli yayiliyor
- Deck reflow calisiyor (2 sekil sola kayiyor)
- Kamera FOV 10 -> 9.2 ile drum kare genisliginin %63'unden %80'ine cikarilmis (referans %76)
  **[2026-08-25 duzeltildi]** Bu %80 sonradan %100'e tasti: drum kareyi iki kenardan da tasiriyordu.
  Asagidaki "Cerceve ve oynanabilirlik turu" bolumune bak.

## Kapsam disi (duzeltilmeyecek)
Referansin meta-UI katmani (level rozeti, ayar dislisi, SPIN butonu, 3x3 sekil izgarasi) —
case dokumani meta UI istemiyor, staged sahnede de yok.


---

# Cerceve ve oynanabilirlik turu (2026-08-25)

Iki onaylanan bulgu tek bir kok sebepten cikti ve tek bir kati oteleme ile duzeltildi:
`Drum` koku (0.00, 2.77, 7.05) -> **(0.00, -0.52, 30.39)**, yani kamera ekseninde 23.5 birim
geriye ve 3.3 birim asagiya. `Case1Adjust.ReelBackToReferenceFrame` bunu her kosuda sahneden
YENIDEN turetir; ikinci kosu reel'i 0.00 birim oynatir.

## Onaylanan bulgu 1 — tepsi drum'in ARKASINDAYDI, sahne oynanamazdi

Tepsinin ust iki siri (dokuz parcanin altisi, **basilabilir on siranin ucu de dahil**) drum'in
hucrelerinden daha uzaktaydi ve uzerlerine boyaniyordu. `ShapeTapInput.PickTrayShape` ekran
yakinligina gore sectigi icin sahne yalnizca drum'a basilarak "oynanabiliyordu".

| olcum (sahne grafiginden, `Case1FramingProbe`) | once | sonra |
|---|---|---|
| gizlenen tepsi parcasi | **6 / 10** | **0 / 10** |
| gizlenen ON SIRA parcasi (slot 0/1/2) | **3 / 3** | **0 / 3** |
| min(drum derinligi) - max(tepsi derinligi) | **-5.17** | **+11.37** |

Engelleyiciler ada ada isimlendirildi: `Segment_c1_r13`, `Segment_c3_r13`, `Segment_c2_r12`.

`IsInFrontRow`in TERS oldugu iddiasi dogrulanmadi: slot 0/1/2 zaten dogru sira; gorulmez
olmalarinin sebebi eslesmenin tersligi degil, drum'in onlerinde durmasiydi. Drum geri
cekilince ayni uc slot hem gorunur hem basilabilir hale geldi. `Case1PlayabilityGate` ile
olculdu: **GREEN, passed=10 failed=0 accepted=3 rejected=7** — uc on-sira parcasi da
`HandlePieceTap` ile kabul edildi ve kendi hucresine oturdu (rest mesafesi 0.017-0.054 u),
arkadaki yedi parca dogru gerekcelerle reddedildi.

## Onaylanan bulgu 2 — drum kareyi tasiriyordu

Canli sira tarama cizgisinde, gercek yakalanan karede olculdu:

| | once | sonra | referans (`CASE1_TEPSI.png`) |
|---|---|---|---|
| canli sira genisligi | x[0..1079] = **1080 px (%100)** | x[132..944] = **813 px (%75.28)** | x[149..963] = **815 px (%75.46)** |
| x=0 pikseli | (115,208,106) yesil Square hucresi | arka plan | arka plan |
| ok basliklari | ikisi de kare DISINDA (px 1138.6 ve -62.1) | ikisi de ICERIDE (px 917.7 ve 160.0) | ikisi de iceride |
| ok basligi rengi | - | rgb(134,193,240) | rgb(134,193,240) |
| beyaz cerceve raylari (py) | - | 358..364 / 514..521 | 358..364 / 515..521 |
| serit merkezi (py) | 864.3 | **439.5** | **439.5** |

## Hala sapma olan, bu turda ONAYLANMAYAN
- Tepsi sira yuksekligi: bizde on sira 123 px, arka siralar 76/90 px (`FrontScale` Y x1.50,
  `BackScale` Y x0.60). Referansta uc sira da **esit**: 71 / 66 / 69 px. Koddaki
  "front row 155/148/149 px, back rows 104/113/110" yorumu bu referans karesiyle uyusmuyor.
- Tepsi sutun araligi: bizde px 377/540/703, referansta 403/540/676.
- Drum'in dikey konumu: bizde bbox y 276..786, referansta 201..718. Serit ikisinde de py 439.5,
  yani fark drum'in serit uzerinde/altinda kac sira gosterdiginde.
- `BuildShapeTray` **idempotent degil**: ard arda bes Build'de tepsi zemini
  y = 1.754 -> 1.148 -> 0.513 -> -0.153 -> -0.852 diye kayiyor (adim -0.61, -0.64, -0.67, -0.70,
  yani buyuyor). Ekran konumlari her seferinde korunuyor (raycast viewport noktasini sabit
  tutuyor), bu yuzden karede gorunmuyor; ama tepsinin derinligi her Build'de ~2.3 artiyor ve
  bulgu 1'in derinlik payini yiyor (16.05 -> 11.37, iki Build'de). Bes-alti Build sonra
  I-A yeniden kirmiziya doner. Ayri bir is olarak ele alinmali.
