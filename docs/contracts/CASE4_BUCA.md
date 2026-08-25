# Case 4 — Buca Implementation Contract & Source of Truth

> [!IMPORTANT]
> **Tek Doğru Kaynak: `_refs/Developer Case Referans/Buca.mp4` Referans Videosu**
> Case PDF belgesindeki (`Game Developer Case - ROQ Games.pdf`) Case 4 açıklaması referans video ile uyuşmayan yanlış/çelişkili ifadeler içermektedir.
> Projedeki Case 4 uygulaması (mekanikler, görsel efektler, renkler, akış) **yalnızca ve doğrudan `Buca.mp4` referans videosuna** göre inşa edilmiştir.

---

## 1. Referans Akış Kuralları (`Buca.mp4`)

1. **Atış ve Ray Reaksiyonu**:
   - Puck fırlatıldığında beyaz olan arena rayı canlı camgöbeği (cyan) rengine döner.
   - Puck sağ eğri boyunca ilerler ve sol taraftaki yeşil blok yığınına çarpar.

2. **Yeşil Blok Yığını (Stack Collision & Scatter)**:
   - Puck temasıyla birlikte yeşil küpler fiziksel olarak dağılır, döner ve sahaya saçılır.
   - **Renk Sabitliği**: Bloklar dağılırken ve dururken **yeşil** kalır (PDF'te bahsedilen renk değiştirme / macenta geçişleri referansta yoktur, uygulanmaz).

3. **Altın Para Akışı (Coin Stream Payout)**:
   - Puck'ın bloklara temas ettiği noktadan altın paralar yay çizerek (arc) sağ üstteki HUD seviye göstergesine / coin bankasına doğru akar.

4. **HUD & Bitiş**:
   - Paraların ulaşmasıyla birlikte üstteki seviye pipsleri dolar ve seviye numarası güncellenir.
   - Ekranda devasa "LEVEL COMPLETE" afişi yerine referanstaki temiz UI akışı korunur.
