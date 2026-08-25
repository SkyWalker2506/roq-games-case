# Unity CLI ile ROQ Case Geliştirme — Genişletilmiş Dersler, Çalışma Protokolü ve Edge-Case Raporu

**Hazırlayan:** GPT-5.6 Sol — OpenAI
**Kaynak dosya:** `DERSLER(1).md`
**Amaç:** Case 1 sırasında gerçekten yaşanmış hataları koruyarak, bunları Case 2–4 ve ilerideki Unity CLI çalışmalarında kullanılabilecek daha katı bir mühendislik protokolüne dönüştürmek; yeni edge-case senaryolarını ayrıca tanımlamak.

---

## 0. Bu rapor nasıl okunmalı?

Bu raporda iki farklı bilgi sınıfı vardır ve birbirine karıştırılmamalıdır:

1. **YAŞANMIŞ / KAYNAKTA KANITLI:** `DERSLER(1).md` içinde gerçekten yaşandığı yazılan olaylar. Bunlar tarihsel bulgudur.
2. **YENİ / ÖNGÖRÜLEN EDGE CASE:** Kaynaktaki kök sebeplerden türetilen, Unity Editor/CLI otomasyonunda aynı tür regresyona yol açabilecek ek senaryolar. Bunların Case 1'de yaşandığı iddia edilmez; önleyici mühendislik maddeleridir.

Bu ayrım kritik. Kaynak dosyanın en önemli derslerinden biri zaten “sayıların ve iddiaların kaynağını etiketle” kuralıdır. Bu rapor da aynı disipline uyar.

---

# 1. Yönetici özeti — 544 satırlık hata günlüğünün aslında söylediği 10 kök sebep

Kaynakta onlarca ayrı hata var; fakat çoğu aşağıdaki 10 kök sebebin farklı belirtileri:

### 1. Yanlış otorite modeli

Sahnenin, kurucunun, prefab'ın, runtime component'in ve kameranın hangisinin hangi değerin sahibi olduğu baştan belirlenmediğinde aynı veriyi birden fazla sistem yazıyor. Sonuç: sahne kayıyor, prefab override oluşuyor, serialized değer C# default'unu eziyor, tween başka tween'i siliyor.

### 2. Otomasyonun çıktısını tekrar girdi olarak kullanmak

Builder sahneye/asset'e değer yazıyor; sonraki çalıştırma o değiştirilmiş değeri “başlangıç” sanıyor. Böylece her build bir öncekinden biraz daha farklı çıkıyor. Kaynakta bunun ölçülmüş örneği `0.0452 → 0.0395 → 0.0331 → 0.0253`.

### 3. Başarı kriterinin eksik olması

Sadece kadraj ölçülünce dünya bozulabiliyor; sadece merkez piksel rengi ölçülünce shader yanlış olabiliyor; sadece “target bulundu” ölçülünce yanlış target'a gidilebiliyor. Ölçülmeyen alan sessizce çürüyor.

### 4. Görsel problemleri kod problemi sanmak veya tersini yapmak

Aynı `localScale` eşit ekran yüksekliği demiyor. Kamera/perspektif, mesh doğal boyutu ve parent transform sonucu değiştiriyor. Aynı şekilde “kamera yanlış” sanılan sorun dünya yerleşimi olabiliyor.

### 5. Kimliği isim/string ile taşımak

`Hexagon2`, `Hexagon-Hole`, token parsing ve isim tabanlı cleanup sessiz yanlış eşleşmelere yol açıyor. Sabit kümeler enum/ID ile temsil edilmeli.

### 6. Editör durumunu stateless sanmak

Scene dosyası, prefab override'ları, serialized component değerleri, generated asset'ler, material instance'ları ve açık Editor oturumu kalıcı durum taşır. “Kodu geri aldım ama sorun gitmedi” bunun sonucu.

### 7. Aynı özelliğe birden fazla yazar vermek

İki tween aynı `localScale`'a, builder ve Animator aynı transform'a, Rigidbody ve tween aynı position'a yazarsa davranış zamanlamaya bağlı hale gelir. Son yazan kazanır; bu da deterministik tasarım değildir.

### 8. Ölçüm kaynağını/provenance'ı kaybetmek

`VIDEO_MEASURED`, `VISUAL_ESTIMATE`, `TUNED`, `OUR_CAPTURE` gibi etiketler olmadan rakam zamanla “referans ölçümü” diye yeniden anlatılabilir. Kaynakta 0.70 s örneği tam olarak bunu gösteriyor.

### 9. CLI çalışma yaşam döngüsünü yanlış yönetmek

Hub'ın Editor açması batchmode'u bloke ediyor; `-quit` play-mode capture'ı öldürüyor; save sırası yanlışsa log doğru görünürken disk eski kalıyor. CLI yalnız komut çalıştırmak değildir; Editor state machine yönetimidir.

### 10. Görsel kalite önceliğini ters kurmak

Kaynağın vardığı doğru sıra: kompozisyon → siluet/ölçek → renk kimliği → materyal/kontur → hareket/juice → ses. Yanlış yerleşime squash-stretch eklemek yanlış görüntüyü yalnızca daha enerjik hale getirir.

---

# 2. Birbirine çelişiyor gibi görünen kuralların kesinleştirilmesi

Kaynak dosyada zaman içinde edinilen dersler üst üste yazıldığı için bazı ifadeler ilk bakışta çelişkili okunabilir. Bunları tek bir modelde birleştirmek gerekir.

## 2.1 “Önce dünya, sonra kamera” ile “sahne otoritedir” çelişmiyor

Doğru nihai yorum:

- **Dünya yerleşimi insan-authored sahnede yaşar.**
- Sahne dünya mantığıyla dizilir: ortak zeminler, mantıklı hierarchy, niyetli rotasyonlar, ölçülmüş aralıklar.
- **Kamera bu authored dünyayı kadrajlar.**
- Builder yerleşimi yeniden çözmeye çalışmaz; kimlik, renk, variant, eşleşme, generated visual ve validation üretir.

Yani “world-first” bir **tasarım ilkesi**, “scene is authority” ise bunun **depolama/ownership kararıdır**.

## 2.2 “Kamera yokmuş gibi diz” ile “kamera tek doğruluk kaynağıdır” ifadesi ayrıştırılmalı

Kamera:

- **Dünya konumunun otoritesi değildir.**
- Ancak **ekran-uzayı ölçümünün otoritesidir.**

Örneğin bir mesh'in ekranda hangi local ekseni “yükseklik” olarak okunuyor sorusunda kamera kullanılabilir. Fakat “objeyi viewport x=0.42'ye denk gelecek world noktasına koy” şeklinde placement yapılmamalı.

Kısa kural:

> **Dünya yerleşimi world-space'te; görsel doğrulama screen-space'te.**

## 2.3 “Dünyada düzelt” ile “ekran yüksekliğini projeksiyonla çöz” çelişmiyor

İki farklı problem vardır:

- **Yerleşim/topoloji:** world-space authority.
- **Oyuncunun gördüğü boyut/hizalama:** screen-space measurement.

Aynı role sahip farklı mesh'ler doğal boyut olarak farklıysa, aynı localScale vermek yanlış olabilir. Bu durumda scale'in sonucu projection ile doğrulanır. Ancak obje world grid'den koparılmaz.

## 2.4 “Build öncesi scene checkout” kuralı güvenli hale getirilmeli

Kaynakta `git checkout -- Scene.unity` temiz başlangıç için öneriliyor. Bu, yalnız canonical baseline'ın Git'te güvenli biçimde bulunduğu ve kullanıcı değişikliğinin olmadığı durumda güvenlidir.

**Yeni daha güvenli kural:**

1. Working tree dirty mi kontrol et.
2. Kullanıcının authored değişikliği varsa otomatik checkout yapma.
3. Önce snapshot/checkpoint al.
4. Hangi dosyaların builder tarafından yazılmasına izin verildiğini whitelist et.
5. Gerekirse yalnız generated scene veya test fixture resetlensin.

Bir CLI agent'ı kullanıcının elle yaptığı scene düzenini “temiz build” uğruna silememeli.

---

# 3. Otorite ve ownership tablosu — her veri tek bir yerde yaşamalı

| Veri / davranış | Tek otorite | Yasak ikinci yazar |
|---|---|---|
| World position/rotation/scale | Authored Scene | Builder viewport solver |
| Kamera/FOV | Authored Scene / explicit camera profile | Per-object placement code |
| Shape identity | `ShapeId` / data profile | GameObject name parsing |
| Shape-color ilişkisi | Prefab Variant / data profile | Scene instance material override |
| Generated object kimliği | Marker component + stable ID | İsim/suffix tahmini |
| Runtime sequence timing | Tek timing profile | Scene serialized eski değer + C# default yarışması |
| `Transform.position` during flight | Tek sequence owner | Rigidbody/Animator/ikinci tween |
| `Transform.localScale` during effect | Tek composed tween | Reflow tween + squash tween yarışması |
| Rigidbody movement | Physics solver | Transform tween |
| Visual grade | Case-specific Volume/material | Birden fazla üst üste global volume |
| Reference measurement | Measurement manifest | Yorum içindeki kaynaksız sabit |
| Builder input assets | Read-only input folder | Builder write-back |
| Builder outputs | Generated/output folder | Base prefab klasörü |
| Capture outputs | Run-ID klasörü | Aynı dosya adını ezme |

Bu tablo bir stil önerisi değil; regresyonu önleyen bir mimari sözleşmedir.

---

# 4. Unity CLI için önerilen çalışma protokolü

Aşağıdaki akış Case 2–4 başlamadan standart hale getirilmelidir.

## Aşama 0 — PRE-FLIGHT

Her CLI operasyonundan önce:

1. **Unity sürümü** kaydedilir.
2. **Project path** doğrulanır.
3. **Aktif Editor süreci** var mı bakılır.
4. Kullanıcının Editor'üne bağlanılabiliyorsa running-editor CLI tercih edilir; batchmode CI/izole iş için kullanılır.
5. `git status` alınır.
6. Dirty input/prefab/scene varsa snapshot/checkpoint alınmadan destructive reset yapılmaz.
7. Target scene GUID/path doğrulanır.
8. Package/import durumu tamamlanmış mı doğrulanır.
9. Önceki capture klasörleri “latest” diye varsayılmaz.
10. Yeni bir `RUN_ID` oluşturulur.

Önerilen Run ID:

`YYYYMMDD-HHMMSS_commitShortHash_caseName`

## Aşama 1 — INSPECT / DUMP

Kod değiştirmeden önce gerçek nesne ölçülür:

- hierarchy,
- local/world transform,
- parent chain,
- lossyScale,
- renderer bounds,
- collider bounds,
- prefab bağlantısı,
- material/sharedMaterial,
- serialized component values,
- camera transform/FOV/aspect,
- active post-process volumes,
- Animator/Rigidbody varlığı,
- generated marker sayısı.

**Kural:** İlk teşhis turunda hiçbir şeyi “muhtemelen” diye düzeltme. Önce dump.

## Aşama 2 — PLAN / DRY-RUN

Builder doğrudan yazmadan önce ne yapacağını raporlamalı:

- kaç nesne buldu,
- kaç nesne oluşturacak,
- kaçını silecek,
- hangi asset'lere yazacak,
- hangi input dosyalarına dokunmayacak,
- beklenen minimum/maksimum count.

Örnek:

`Expected 5 slot plates; found 0` → **FAIL**

`Expected 5; found 5; will modify 5` → devam.

Bu aşama “hiçbir şey bulamayan döngünün başarılı rapor etmesi” sorununu sınıfsal olarak çözer.

## Aşama 3 — ATOMIC APPLY

Değişiklik mümkün olduğunca atomik yapılmalı:

1. Plan assert'leri geçer.
2. Mutation başlar.
3. Yalnız whitelist dosya/objelere yazılır.
4. Bir hata olursa yarım state bırakmadan rollback edilir.
5. Generated objeler marker + stable ID alır.
6. Scene/prefab/asset save sırası açık biçimde uygulanır.

## Aşama 4 — COMPILE / IMPORT / SAVE

Sıra önemlidir:

1. Asset refresh/import tamamlanır.
2. Script compile tamamlanır.
3. Compile error varsa görsel teste geçilmez.
4. Shader compile hataları ayrıca taranır.
5. Scene dirty state kontrol edilir.
6. Scene kaydedilir.
7. AssetDatabase/Prefab save tamamlanır.
8. Diskteki dosya hash'i değişmiş mi kontrol edilir.

“Logda yaptım yazıyor ama ekran aynı” durumunda ilk şüphelerden biri save sırası olmalıdır.

## Aşama 5 — DETERMINISTIC CAPTURE

Capture için:

- doğru scene,
- doğru camera,
- sabit çözünürlük,
- sabit aspect,
- sabit capture FPS,
- sabit random seed gerekiyorsa seed,
- aynı quality/render pipeline,
- aynı post-process layer mask,
- başlangıç state reset,
- stale `DontDestroyOnLoad` objesi yok,
- output yeni Run ID klasörüne.

Capture'ın içine metadata yazılmalı:

- commit hash,
- run ID,
- Unity version,
- scene path/GUID,
- reference hash,
- capture resolution/FPS,
- color space,
- quality level.

## Aşama 6 — DÖRT KATMANLI VALIDATION

Tek “PASS” yerine dört ayrı sınıf:

### 6.1 Structural Gate

- hierarchy doğru,
- tek root,
- duplicate generated yok,
- prefab linkleri bağlı,
- input asset değişmemiş,
- ortak zemin Y toleransı içinde,
- beklenen integer/intentional rotation,
- unexpected override yok.

### 6.2 Functional Gate

- interaction doğru target'a gidiyor,
- target ID doğru,
- input doğru objeye gidiyor,
- sequence sonunda state tutarlı,
- exception yok.

### 6.3 Visual Gate

- projection bbox,
- object height/width,
- silhouette IoU,
- color ΔE,
- local ROI karşılaştırması,
- shader/material yakın çekim,
- side-by-side frame.

### 6.4 Temporal Gate

- motion start,
- contact,
- reaction peak,
- settle gibi event timestamp'leri,
- toplam sequence süresi,
- frame tolerance.

Bu kapılar birbirinin yerine geçemez.

## Aşama 7 — NEGATIVE CONTROL

Her gate'in gerçekten kırmızı dönebildiği kanıtlanmalı.

Örnek:

- target ID geçici bilinçli yanlış ver → Functional Gate kırmızı mı?
- renk ROI'sini bilinçli değiştir → ΔE kırmızı mı?
- generated duplicate ekle → Structural Gate yakalıyor mu?

Her zaman PASS olan kontrol süstür.

## Aşama 8 — IDEMPOTENCE TEST

Builder aynı baseline üzerinde iki kez çalıştırılır.

Beklenti:

- generated count aynı,
- transforms aynı,
- material values aynı,
- scene hash aynı veya yalnız nondeterministic metadata farkı,
- input asset hash'leri aynı.

Birinci ve ikinci run farklıysa teslim yapılmaz.

## Aşama 9 — DIFF AUDIT

Sonunda:

- `git diff --stat`,
- değişen dosya listesi,
- input klasöründe değişiklik var mı,
- `.meta` GUID değişmiş mi,
- scene/prefab unexpected override var mı,
- generated output dışında binary değişiklik var mı

kontrol edilir.

## Aşama 10 — STOP / ROLLBACK

Aynı metrikte üç tur iyileşme yoksa:

1. DUR.
2. Son üç denemeyi karşılaştır.
3. Hipotezi yaz.
4. Yeni ölçüm ekle.
5. Yaklaşımı değiştir.

Dördüncü kez aynı sayıyı farklı sabitle denemek “iteration” değildir.

---

# 5. Kaynaktaki derslerin yeniden sınıflandırılmış hali

## 5.1 Sahne ve kalıcı durum

Kaynakta kanıtlı sorunlar:

- Kod rollback sahneyi rollback etmedi.
- Builder sahneye yazdığı state'i sonraki build'de tekrar girdi yaptı.
- Save'den sonra çalışan pass diske yazılmadı.
- Serialized runtime değerler C# initializer'larını ezdi.

**Birleşik kural:** Unity projesi stateless değildir. Scene/prefab/asset/serialized değerlerin her biri ayrı state deposudur.

## 5.2 Generated object lifecycle

Kaynakta:

- glif kopyaları birikti,
- eski `Hexagon2` sahnede kaldı,
- cleanup yanlış root'ta aradı.

**Birleşik kural:** Generated objeler isimle değil marker component + stable generated ID ile yönetilir.

Önerilen ID mantığı:

`Case3.StickerPile.Layer02.Item05`

İsim değişse bile lifecycle bozulmaz.

## 5.3 Prefab ve material ownership

Kaynakta:

- `Object.Instantiate` prefab linkini kopardı,
- instance material override prefab variant mantığını bozdu,
- builder base prefab input'unu değiştirdi.

**Birleşik kural:** Input prefab read-only; output variant ayrı klasörde; scene instance yalnız placement/state taşır.

## 5.4 Identity/data modeling

Kaynakta:

- string parse yanlış eşleşti,
- paralel tablolar drift etti.

**Birleşik kural:** Kimlik strongly typed; ilişki tek veri kaynağından türetilir.

## 5.5 Ölçüm ve gate tasarımı

Kaynakta:

- center-pixel gate shader'ı göremedi,
- kadraj gate dünya bozukluğunu göremedi,
- aggregate metric farklı crop'larda yanlış sonuç verdi,
- threshold değiştirerek gate'i yeşillendirme riski görüldü.

**Birleşik kural:** Gate, üretim kodundan bağımsız bir oracle olmalı ve yalnız iddia ettiği şeyi PASS etmelidir.

## 5.6 Transform/tween ownership

Kaynakta:

- reflow scale tween'i squash tween tarafından ezildi,
- eksen tahmini yanlış çıktı,
- aynı scale farklı mesh/perspektifte farklı ekran sonucu verdi.

**Birleşik kural:** Bir property'nin bir frame aralığında tek yazarı vardır. Screen outcome gerekiyorsa screen-space doğrulanır.

## 5.7 Shader/render

Kaynakta:

- particle shader RGB'yi material'dan alırken particle gradient değiştirilmişti,
- SRP Batcher CBUFFER mismatch magenta üretti,
- efekt görünmemesinin aynı anda dört nedeni vardı.

**Birleşik kural:** Render bug'larında pipeline katmanlarını ayrı test et: geometry → depth → material → vertex/color channel → lighting → post → lifetime.

## 5.8 CLI/editor lifecycle

Kaynakta:

- Hub Editor açtı ve batchmode'u kilitledi,
- `-quit` capture'ı öldürdü,
- batchmode kullanıcı Editor'ünü engelledi.

**Birleşik kural:** Running-editor otomasyonu ile CI batchmode ayrı ürünler gibi ele alınmalı.

---

# 6. YENİ EDGE-CASE KATALOĞU

Aşağıdaki maddeler kaynak dosyada yaşanmış olarak geçmez. Bunlar mevcut derslerin kök sebeplerinden türetilen yeni koruma senaryolarıdır.

---

## Grup A — Scene / Prefab / Serialization

### EC-A01 — Field rename sonrası eski serialized değer yaşamaya devam eder

**Senaryo:** `rippleDelay` adı değişir veya tip değiştirilir; sahnede eski serialized state beklenmedik davranış üretir.

**Belirti:** C# default doğru, inspector/runtime farklı.

**Kontrol:** SerializedObject dump + component recreation A/B.

**Önlem:** Kritik profile verisini scene field yerine versioned ScriptableObject/profile'da tut; migration açık yazılsın.

### EC-A02 — Prefab Variant base değişir fakat instance override eski değeri gizler

**Belirti:** Prefab düzeltildiği halde bazı scene instance'ları eski görünür.

**Kontrol:** `PrefabUtility.GetPropertyModifications` raporu.

**Önlem:** Unexpected override gate.

### EC-A03 — Prefab Stage açıkken komut yanlış sahneyi değiştirir

**Belirti:** CLI “başarılı”, ana scene değişmemiş veya prefab beklenmedik değişmiş.

**Kontrol:** Aktif stage/path assert.

### EC-A04 — Disabled child cleanup/scan dışında kalır

`GetComponentsInChildren<T>()` çağrısı `includeInactive` seçimine göre eski generated objeyi kaçırabilir.

**Önlem:** Lifecycle scan her zaman inactive generated objeleri de kapsasın.

### EC-A05 — Nested prefab root'u silerken başka authored child kaybolur

Generated root içine insan-authored nesne taşınmışsa “root'u sil yeniden üret” kullanıcı emeğini siler.

**Önlem:** Generated root altında yalnız marker'lı child kabul eden structural gate.

### EC-A06 — Non-uniform parent scale child fizik/material davranışını bozar

`lossyScale`, collider, normal ve particle size beklenmedik değişir.

**Kontrol:** Parent chain scale audit.

### EC-A07 — Negative/mirrored scale culling ve normal yönünü ters çevirir

Özellikle inverted hull outline ve tek yüzlü sticker/shader için kritik.

**Kontrol:** determinant/negative axis gate.

---

## Grup B — CLI / Editor lifecycle

### EC-B01 — Domain Reload sonrası CLI referansı geçersiz olur

Komut sırasında script compile tetiklenirse domain reload tool callback'ini kesebilir.

**Önlem:** Compile boundary öncesi checkpoint; reload sonrası command resume ID.

### EC-B02 — Enter Play Mode Options ile Domain Reload kapalıysa static state taşınır

**Belirti:** İlk play doğru, ikinci play duplicate event/subscriber nedeniyle iki kez çalışır.

**Kontrol:** Aynı play testini art arda iki kez çalıştır.

**Önlem:** Static reset hook + subscription idempotence.

### EC-B03 — `EditorApplication.delayCall` veya async operation bitmeden komut tamamlandı sanılır

**Belirti:** Log “queued”, CLI “success”; gerçek iş daha sonra veya hiç çalışmıyor.

**Önlem:** Explicit completion token/future; yalnız queue etmek başarı değildir.

### EC-B04 — Asset import worker bitmeden capture başlar

**Belirti:** İlk capture pink/default material, ikinci capture doğru.

**Önlem:** Import/compile/shader-ready barrier.

### EC-B05 — Kullanıcı play mode'dayken builder scene mutasyonu yapar

Runtime clone ile edit-time scene karışabilir.

**Önlem:** Command başında `isPlaying/isPlayingOrWillChangePlaymode` policy assert.

### EC-B06 — CLI crash yarım scene mutation bırakır

**Önlem:** Transaction snapshot + atomic save + failure rollback.

### EC-B07 — Aynı komut iki terminal/agent tarafından eşzamanlı çalıştırılır

**Belirti:** duplicate generated objeler, asset write race.

**Önlem:** Project-level mutation lock + run owner ID.

### EC-B08 — UPM/package resolution devam ederken script compile denenir

**Önlem:** Package manager settled barrier.

---

## Grup C — Animation / Tween / Physics

### EC-C01 — Animator ile tween aynı transform'u yazar

Tween kodu doğru görünür fakat Animator her frame geri yazar.

**Kontrol:** Animator-bound property audit.

**Önlem:** Ownership manifest; tween sırasında Animator layer/property etkisi kapatılır veya animation parametresi sürülür.

### EC-C02 — Rigidbody ile transform tween yarışır

Özellikle Buca gibi fizik case'inde transform yazmak solver momentumunu bozar.

**Önlem:** Dynamic Rigidbody aktifken position/rotation transform write gate.

### EC-C03 — Rigidbody interpolation capture ölçümünü kaydırır

Visual transform ile physics state aynı frame olmayabilir.

**Önlem:** Timing gate'in neyi ölçtüğü tanımlansın: rendered transform mı physics contact mı?

### EC-C04 — `Time.timeScale` / `fixedDeltaTime` testler arasında kalır

Hitstop veya debug test sonrası sonraki capture timing'i bozulabilir.

**Önlem:** Capture preflight'ta time settings reset/assert.

### EC-C05 — Tween object disable/destroy sonrası tamamlanma callback'i çalışır

Eski callback yeni instance'a veya silinmiş objeye state yazabilir.

**Önlem:** cancellation token / generation ID.

### EC-C06 — Coroutine iki kez başlar

Input double tap veya duplicate subscription sequence'i paralel başlatır.

**Önlem:** interaction state machine + reentrancy gate.

### EC-C07 — Easing toplam duration doğru olsa da referans event timestamp'leri yanlış

Toplam 0.70 s tutabilir ama contact 150 ms geç olabilir.

**Önlem:** yalnız total değil event marker gate.

### EC-C08 — Physics sonucu frame-rate/solver setting ile değişir

**Önlem:** solver iterations, fixed timestep, collision mode ve material combine profile'a dahil edilmeli.

---

## Grup D — Shader / Material / Post Process

### EC-D01 — `renderer.material` sessiz material clone üretir

**Belirti:** memory/material sayısı artar; shared material değişikliği bazı instance'lara gitmez.

**Önlem:** `sharedMaterial` + MaterialPropertyBlock politikası; clone count gate.

### EC-D02 — MaterialPropertyBlock eski property'yi taşır

Bir property sonraki pass'te set edilmezse eski MPB değeri yaşayabilir.

**Önlem:** MPB clear/reset veya tam property contract.

### EC-D03 — Birden fazla Global Volume üst üste grade uygular

**Belirti:** Kodda saturation +4, görüntü beklenenden çok farklı.

**Kontrol:** aktif Volume/priority/weight/layer mask dump.

### EC-D04 — SceneView doğru, GameView yanlış veya tersi

Post, camera stack, render feature veya layer mask farkı.

**Önlem:** Görsel gate yalnız gerçek gameplay camera capture'ından.

### EC-D05 — Shader variant Editor'de var, build'de strip edilir

**Önlem:** final standalone/device build smoke test; yalnız Editor capture yeterli değil.

### EC-D06 — Gamma/Linear veya sRGB import flag değişir

**Belirti:** ΔE bir anda yükselir; palette kodu değişmemiştir.

**Önlem:** Project color space + texture sRGB flag manifest/gate.

### EC-D07 — Transparent sorting hierarchy/sibling değişimiyle bozulur

Sticker, overlay ve particle case'lerinde kritik.

**Önlem:** sorting layer/order/depth explicit; hierarchy order'a bağımlı bırakma.

### EC-D08 — Z-fighting yalnız bazı kamera açılarında görünür

**Önlem:** close-up angle matrix; depth bias'i tek çözüm sanma.

### EC-D09 — First-frame shader warm-up capture'ı kirletir

**Önlem:** warm-up frame veya shader variant preload.

### EC-D10 — HDR VFX bloom'u istemeden UI/material'a taşır

**Önlem:** HDR değerleri yalnız intentional VFX; white plastic 0–1 LDR bandında.

---

## Grup E — Görsel ölçüm / Capture

### EC-E01 — Stale capture “latest” diye okunur

Kaynakta `-quit` yüzünden eski frame ölçülmesi yaşandı. Bunun geneli:

**Önlem:** output klasörü run-ID bazlı; her raporda capture timestamp + commit hash doğrulanır.

### EC-E02 — Referans video değişti, ölçüm manifest'i eski kaldı

**Önlem:** reference file SHA-256 hash'i measurement manifest'e yazılır.

### EC-E03 — Letterbox/crop farkı viewport ölçümünü bozar

**Önlem:** önce active image rectangle normalize edilir; sonra bbox ölçülür.

### EC-E04 — ROI yanlış hizalı olduğu için ΔE aslında layout hatasını ölçer

Renk karşılaştırmadan önce ROI registration gerekir.

**Önlem:** color gate ve layout gate ayrılır; gerekirse aligned ROI kullanılır.

### EC-E05 — Anti-aliasing silhouette threshold'unu değiştirir

**Önlem:** bbox/IoU ölçümünde alpha/edge threshold profile'a bağlı sabitlenir.

### EC-E06 — Parlak particle bbox'ı ana objenin bounds'una karışır

**Önlem:** measurement layer/mask veya semantic renderer seti.

### EC-E07 — Aggregate metric doğru, lokal hata büyük

Kaynakta drum silhouette vs tek hücre örneği yaşandı.

**Önlem:** global + local ROI birlikte raporlanır.

### EC-E08 — Aynı çözünürlük ama farklı renderScale / dynamic resolution

**Önlem:** renderScale/dynamic resolution capture metadata'sına dahil.

### EC-E09 — Retina/HiDPI screenshot fiziksel piksel ile logical point'i karıştırır

**Önlem:** yalnız render target pixel dimension authority.

### EC-E10 — Bir gate yanlış referans frame'ini kullanır

**Önlem:** her temporal gate `referenceFrameIndex` ve timestamp manifest'inden okumalı; dosya adına bakarak tahmin etmemeli.

---

## Grup F — Git / Asset / Dosya bütünlüğü

### EC-F01 — `.meta` kaybolur ve GUID değişir

**Belirti:** prefab/material reference missing.

**Önlem:** file move/copy Unity-aware yapılır; meta diff gate.

### EC-F02 — Case-sensitive path macOS/Windows farkı

`Assets/Case1` vs `Assets/case1` bir ortamda çalışıp diğerinde kırılabilir.

**Önlem:** canonical path assertion.

### EC-F03 — Git LFS pointer asset yerine gerçek binary beklenir

**Önlem:** required binary asset hydration preflight.

### EC-F04 — Generated output yanlışlıkla input klasörüne yazılır

**Önlem:** input/output path policy + post-run git diff audit.

### EC-F05 — Otomatik regex edit birden fazla eşleşmeyi değiştirir

Kaynakta benzeri yaşandı.

**Önlem:** exact anchor + expected match count assert; 1 bekleniyorsa 0 veya 2 = FAIL.

### EC-F06 — Compilation başarılı ama warning yeni regressyonu haber verir

**Önlem:** kritik warning allowlist; yeni warning sayısı gate.

---

## Grup G — Input / Runtime state

### EC-G01 — UI EventSystem world input'u yutar

**Belirti:** Editor testinde click çalışır, gerçek composition'da üst HUD eklenince shape tıklanmaz.

**Önlem:** pointer-over-UI ve raycast layer testleri.

### EC-G02 — Yanlış kamera ile raycast

Multiple camera/camera stack olduğunda `Camera.main` beklenen kamera olmayabilir.

**Önlem:** explicit serialized gameplay camera reference.

### EC-G03 — Collider görsel mesh ile scale sonrası eşleşmez

**Önlem:** renderer bounds vs collider bounds ratio gate.

### EC-G04 — Double tap / multitouch sequence'i iki kez tetikler

**Önlem:** input latch + state machine.

### EC-G05 — `DontDestroyOnLoad` test sistemi ikinci capture'a taşınır

**Önlem:** capture öncesi runtime root inventory ve clean boot.

### EC-G06 — Script Execution Order başlangıç state'ini yarışa sokar

Builder/runtime initializer/component Awake aynı değeri farklı sırada yazar.

**Önlem:** explicit bootstrap sequence; Awake side effects minimize.

---

# 7. Builder tasarımı için yeni öneri: iki fazlı “Plan → Apply” modeli

Kaynak dosyadaki birçok hata builder'ın aynı anda hem keşif hem mutasyon yapmasından çıkıyor.

## Faz 1 — Plan

Hiçbir şey yazmaz.

Üretir:

```text
Target scene: Case3
Expected stickers: 24
Found authored stickers: 24
Generated peel backs to delete: 24
Generated peel backs to create: 24
Prefab variants required: 6
Input assets to modify: 0
Scene transform writes: 0
Warnings: 0
```

## Faz 2 — Apply

Plan hash'i hâlâ geçerliyse yazmaya başlar.

Bu model şunları yakalar:

- yanlış root,
- 0 object found,
- unexpected object count,
- input folder write,
- stale generated object,
- kullanıcı arada sahneyi değiştirmişse stale plan.

Builder'ın “ne yapacağını söylemeden yapması” CLI otomasyonunda gereksiz risk.

---

# 8. Mutation transaction / rollback protokolü

Bir CLI agent şu garantiye sahip olmalı:

> **Ya tüm pass uygulanır ya hiçbiri uygulanmamış gibi kalır.**

Öneri:

1. Önce değişecek dosya listesi çıkar.
2. Hash/snapshot al.
3. Apply yap.
4. Compile/validation geçmezse rollback.
5. Rollback sonrası hash'in baseline'a döndüğünü doğrula.
6. Başarılıysa commit-ready diff bırak.

Özellikle `.unity`, `.prefab`, `.mat` ve generated `.asset` dosyalarının yarım mutation'da kalması ileride teşhisi çok pahalı hale getirir.

---

# 9. Ölçüm manifest'i — her “referans sayısı” için zorunlu kayıt

Önerilen format:

```yaml
metric: case1.hero.total_duration
value: 0.704
unit: seconds
source_type: VIDEO_MEASURED
source_file: Fit The Shape.mp4
source_sha256: ...
start_frame: 54
end_frame: 88
fps_basis: 48.29
method: manual_event_markers
confidence: high
notes: tap-to-settle
```

Başka örnek:

```yaml
metric: case3.target_card.viewport_center_x
value: 0.184
unit: viewport
source_type: VISUAL_ESTIMATE
source_file: Stickerdom.mp4
reference_frame: 102
confidence: medium
tolerance: 0.015
```

Bu yapılmadan `MEASURED`, `EXACT`, `REFERENCE` kelimeleri kod yorumunda kullanılmamalı.

---

# 10. Görsel gate mimarisi — “kapı yeşil ama ekran yanlış” problemine çözüm

Tek skor yasaklanmalı.

## 10.1 Composition Gate

Ölçer:

- ana obje bbox,
- relative center,
- occupied screen ratio,
- object-to-object distance,
- overlap/occlusion.

## 10.2 Identity Gate

Ölçer:

- doğru hero ID,
- doğru target ID,
- doğru layer/slot/column.

Bu gate özellikle “mekanik çalışıyor ama referanstaki obje değil” durumunu yakalar.

## 10.3 Material/Local Appearance Gate

Center pixel yerine ROI:

- Lab mean/std,
- edge contrast,
- highlight percentile,
- shadow percentile,
- local gradient.

## 10.4 Silhouette Gate

Segmentation/alpha mask ile IoU veya normalized contour distance.

## 10.5 Temporal Gate

Total duration dışında:

- start,
- clear,
- contact,
- peak,
- settle.

## 10.6 Motion Gate

Object center trajectory'si normalize viewport space'te karşılaştırılır.

Bu sayede toplam timing doğru fakat path yanlışsa yakalanır.

---

# 11. Görsel regresyon karar ağacı

Bir değişiklik sonrası görüntü kötüleştiğinde şu sırayla bak:

1. **Yanlış obje/target mı?**
2. **World placement mı?**
3. **Projection/scale mı?**
4. **Material/base color mı?**
5. **Lighting mi?**
6. **Shader/depth/sorting mi?**
7. **Post-process mi?**
8. **VFX mi?**
9. **Capture/measurement mı stale?**

Post-process ilk çözüm olmamalı.

---

# 12. “Efekt görünmüyor” için genişletilmiş teşhis matrisi

Kaynakta boyut, depth, kontrast ve lifetime'ın aynı anda sorun olduğu bir olay yaşandı. Bunu genişletelim.

Bir VFX görünmüyorsa sırayla:

1. GameObject aktif mi?
2. Renderer aktif mi?
3. Layer camera culling mask'te mi?
4. Sorting layer/order doğru mu?
5. Depth test doğru mu?
6. Geometry camera'ya bakıyor mu?
7. Scale yeterli mi?
8. World position yüzün içinde mi?
9. Renk arka planla kontrastlı mı?
10. Shader particle color kanalını gerçekten okuyor mu?
11. Alpha > 0 mı?
12. Lifetime capture penceresine denk geliyor mu?
13. Burst count > 0 mı?
14. Random seed aynı pattern'i saklıyor mu?
15. Post/bloom clipping blob'a mı dönüştürüyor?
16. İlk frame shader warm-up yüzünden mi kaçıyor?
17. Parent scale/simulationSpace sistemi küçültüyor mu?

“Intensity artır” bu listenin en sonlarında olmalı.

---

# 13. Transform ownership manifest'i

Her sequence için rapor üret:

```text
Object: HeroHexagon
position owner: ShapeFlight
rotation owner: ShapeFlight
scale owner: ShapeFlightComposite
Animator writes transform: NO
Rigidbody dynamic: NO
secondary tween writers: 0

Object: DeckItem_C2_R1
position owner: DeckReflow
scale owner: DeckReflowComposite
Squash separate writer: FORBIDDEN
```

Runtime debug modunda aynı property'ye iki owner kayıt olursa error üretilebilir.

---

# 14. Idempotence yalnız builder count değildir

İki ardışık run arasında karşılaştırılması gerekenler:

- scene file hash,
- generated object count,
- generated stable ID set,
- prefab variant count,
- input asset hashes,
- serialized timing profile values,
- material values,
- camera transform,
- important projected bbox metrics.

Scene YAML'da timestamp-benzeri nondeterministic alan varsa semantic snapshot ile karşılaştırılabilir.

---

# 15. Case 2–4 başlamadan önce zorunlu template

Her yeni case için ilk dosya şu olmalı:

## Case Contract

### Interaction

`INPUT → MOTION → CONTACT → REACTION → SETTLE`

### Hero

- ID:
- start role:
- target role:
- authored scene object:

### Authority

- scene placement owner:
- identity owner:
- material owner:
- runtime motion owner:

### Reference manifest

- video hash:
- fps:
- key frames:
- measurement provenance:

### Required gates

- structural:
- functional:
- visual:
- temporal:
- idempotence:

### Forbidden systems

Case brief'in istemediği queue/level/progression/extra HUD açıkça yazılmalı.

Bu, agent'ın scope creep yapmasını engeller.

---

# 16. Case 2–4 için genişletilmiş kontrol listesi

## Başlamadan önce

- [ ] Case brief'teki **tek kısa interaction** tek cümleyle yazıldı.
- [ ] Referans video hash'i kaydedildi.
- [ ] Reference keyframe'ler çıkarıldı.
- [ ] `VIDEO_MEASURED` ve `VISUAL_ESTIMATE` ayrıldı.
- [ ] Sahne world-space'te insan tarafından mantıklı dizildi.
- [ ] Kamera yalnız kadraj için ayarlandı.
- [ ] Tek root/hierarchy standardı kuruldu.
- [ ] Shape/type identity enum/stable ID ile tanımlandı.
- [ ] Base prefab/input folder read-only kabul edildi.

## CLI preflight

- [ ] `git status` kaydedildi.
- [ ] Kullanıcı değişiklikleri snapshotlandı.
- [ ] Running editor/batchmode çakışması yok.
- [ ] Doğru Unity sürümü.
- [ ] Doğru scene/stage.
- [ ] Package/import/compile settled.
- [ ] Run ID oluşturuldu.

## Builder

- [ ] Önce dry-run plan üretiyor.
- [ ] Expected count ile found count karşılaştırılıyor.
- [ ] Generated objeler marker + stable ID taşıyor.
- [ ] Cleanup isim listesine bağlı değil.
- [ ] Input asset yazılmıyor.
- [ ] Apply atomic/rollback destekli.
- [ ] Save işlemleri doğru sırada.

## Runtime

- [ ] Her transform property'sinin tek owner'ı var.
- [ ] Rigidbody ve tween aynı position'a yazmıyor.
- [ ] Animator transform'u gizlice ezmiyor.
- [ ] Sequence reentrant değil.
- [ ] timeScale/fixedDeltaTime capture öncesi doğru.
- [ ] random seed politikası bilinçli.

## Render

- [ ] Base color/material önce doğru.
- [ ] Volume listesi/priority kontrol edildi.
- [ ] Gamma/Linear ve sRGB import manifestte.
- [ ] Transparent sorting explicit.
- [ ] Shader compile ve SRP Batcher kontrolü temiz.
- [ ] First-frame warm-up düşünülmüş.

## Capture

- [ ] Doğru gameplay camera.
- [ ] Sabit çözünürlük/aspect/FPS.
- [ ] Yeni Run ID klasörü.
- [ ] Commit/reference hash metadata içinde.
- [ ] Stale capture kullanımı imkânsız.

## Gates

- [ ] Structural Gate.
- [ ] Functional/Identity Gate.
- [ ] Composition/Layout Gate.
- [ ] Color/Material ROI Gate.
- [ ] Temporal Gate.
- [ ] Motion trajectory Gate gerekiyorsa.
- [ ] Her gate'in negative-control testi yapıldı.
- [ ] Side-by-side görsel üretildi.

## Kapanış

- [ ] Builder iki kez koşuldu; idempotent.
- [ ] `git diff` beklenen dosyalarla sınırlı.
- [ ] Base/input asset hash'leri değişmedi.
- [ ] Yeni warning/error yok.
- [ ] Aynı metrikte 3 başarısız tur varsa yaklaşım değiştirildi.
- [ ] Son raporda “ne değişti / neden / hangi kanıtla” yazıldı.

---

# 17. Unity CLI agent için zorunlu davranış sözleşmesi

Aşağıdaki kurallar agent prompt'una doğrudan eklenebilir:

1. **Ölçmeden placement/axis/scale tahmin etme.**
2. **Kullanıcının authored scene'ini izinsiz resetleme veya checkout etme.** Önce snapshot al.
3. **Her mutation'dan önce dry-run ve expected-count assert üret.**
4. **İsimleri kimlik olarak kullanma; stable ID/enum kullan.**
5. **Generated objeleri marker ile yaşam döngüsüne al.**
6. **Input prefab/assets read-only; output variant/generated ayrı yerde.**
7. **Aynı transform property'ye iki sistem yazamaz.**
8. **Rigidbody hareketine transform tween ile müdahale etme.**
9. **Runtime serialized field değiştirdiysen scene/profile authority'yi kontrol et.**
10. **“MEASURED/EXACT/REFERENCE” kelimesini provenance olmadan kullanma.**
11. **Gate'i yeşillendirmek için threshold/expected value değiştirme.** Önce kriterin neden yanlış olduğunu kanıtla.
12. **Her gate'in kırmızı olabildiğini negative test ile göster.**
13. **Yan yana referans karşılaştırmasını ilk iterasyonda üret.**
14. **Post-process'i geometri/material hatasını gizlemek için kullanma.**
15. **Bir efekt görünmüyorsa size/depth/contrast/lifetime/channel/sorting'i ayrı kontrol et.**
16. **Save/compile/import tamamlanmadan capture başlatma.**
17. **Capture output'u run-ID ile versionla; eski capture'ı “latest” sanma.**
18. **Aynı yaklaşım üç tur ilerlemiyorsa DUR, teşhis yaz ve yöntemi değiştir.**
19. **Builder'ı iki kez çalıştırıp idempotence kanıtlamadan teslim etme.**
20. **Son diff'te input asset değişmişse bunu hata kabul et; bilinçli migration değilse rollback et.**

---

# 18. Önerilen otomatik rapor formatı

Her Unity CLI turunun sonunda agent şu raporu üretmeli:

```text
RUN: 20260821-143100_a1b2c3_Case2
Unity: 6000.x
Scene: Assets/Case2/Scenes/Case2.unity
Reference SHA: ...

PRE-FLIGHT
- working tree dirty: YES/NO
- editor attached: YES/NO
- package/import ready: YES/NO

PLAN
- expected objects: 12
- found: 12
- create: 4
- delete generated: 4
- input asset writes: 0

APPLY
- compile errors: 0
- shader errors: 0
- scene saved: YES
- generated IDs unique: YES

GATES
- Structural: PASS
- Functional: PASS
- Layout: FAIL (hero x +0.024 viewport)
- Color: PASS (mean dE 4.1)
- Temporal: PASS (contact +1 frame)
- Idempotence: PASS

REGRESSION
- previous layout error: +0.017
- current layout error: +0.024
- result: WORSE

DECISION
- revert latest placement change
- do not touch post-process
- next hypothesis: authored hero scale/perspective
```

Bu format agent'ın “çok şey yaptım” anlatısı yerine kanıt üretmesini sağlar.

---

# 19. Öncelik sırası — Case 2, 3 ve 4'te ne önce yapılmalı?

## P0 — Teslimden önce kesin

- Interaction identity doğru.
- Authored scene/world temiz.
- Builder input/output ayrılmış.
- Stable ID/enum.
- Generated lifecycle sağlam.
- Tek property owner.
- CLI run güvenli/transactional.
- Deterministic capture.
- Structural + Functional + Visual + Temporal gates.
- Idempotence.
- Side-by-side comparison.

## P1 — Kalite için güçlü

- Local material ROI metrics.
- Motion trajectory gate.
- Shader warm-up.
- unexpected prefab override gate.
- Volume stack audit.
- reference hash/manifests.
- negative-scale / parent lossyScale audit.

## P2 — İleri otomasyon

- Automatic rollback.
- semantic scene snapshot diff.
- per-property runtime ownership debugger.
- automated visual registration.
- CI standalone build visual smoke test.

---

# 20. Sonuç

`DERSLER(1).md` dosyasının en değerli tarafı “iyi Unity tavsiyeleri” vermesi değil; aynı yanlış mühendislik kalıplarının tekrar tekrar nasıl farklı belirtiler ürettiğini göstermesidir.

En önemli nihai dönüşüm şudur:

> **Unity CLI agent'ı kod yazan bir araç olarak değil, stateful bir Editor üzerinde ölç–planla–uygula–kanıtla–geri al döngüsü yöneten bir sistem olarak tasarla.**

Case 1'de sorunların çoğu tek bir syntax hatasından çıkmadı. Sorunlar:

- yanlış otorite,
- eksik gate,
- persistent state,
- kaynaksız ölçüm,
- aynı property'ye iki yazar,
- yanlış lifecycle,
- stale capture,
- idempotent olmayan builder

gibi sistemik nedenlerden çıktı.

Case 2–4'te hız kazanmanın yolu daha hızlı kod yazdırmak değil; **yanlış yaklaşımın ikinci build'e ulaşmasını engellemek**.

Bu nedenle bundan sonraki bütün case'lerde başarı ölçütü yalnız “çalıştı” olmamalı:

1. **Doğru şeyi mi çalıştırdı?**
2. **Doğru state'ten mi başladı?**
3. **Doğru objeyi mi değiştirdi?**
4. **Aynı komut tekrar aynı sonucu veriyor mu?**
5. **Referansla fark gerçekten azaldı mı?**
6. **Bunu bağımsız bir gate ve yan yana görsel kanıtlıyor mu?**
7. **Kullanıcının authored çalışmasını korudu mu?**

Bu yedi sorunun cevabı “evet” değilse CLI turu tamamlanmış sayılmamalıdır.
