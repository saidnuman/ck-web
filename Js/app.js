// ASP.NET Core Web API (.NET 10) + JWT Oturum, Favori, KVKK, Yerel Telemetri, Akıllı Header ve Footer Motoru
// (Eski main.js + ek_1.js tek çatı altında birleştirildi — Ana Sayfa ve Kategori Sayfası scroll farkı korunur)

const SITE_CHROME_DEFAULTS = {
    // ===================================================
    // 1. VARSAYILAN ÜST BÖLÜM (HEADER & LOGO) BİLGİLERİ
    // ===================================================
    logo: {
        src: "images/LOGO.png",
        alt: "Site Logosu",
        link: "index.html" // Logoya tıklayınca gidilecek sayfa
    },

    // ===================================================
    // 2. VARSAYILAN ALT BÖLÜM (FOOTER) BİLGİLERİ
    // ===================================================
    footerAciklama: [
        "Yenilikçi dijital çözümlerle geleceği inşa ediyoruz.",
        "Sizlerle birlikte büyümeye ve üretmeye devam ediyoruz."
    ],

    sosyalMedya: [
        {
            isim: "Instagram",
            url: "https://www.instagram.com/cilgin_kirkyamaci/",
            ikon: '<i class="fa-brands fa-instagram"></i>'
        },
        {
            isim: "YouTube",
            url: "https://www.youtube.com/channel/UCG0zYEVBa5Xdhk70Fz9CGrQ",
            ikon: '<i class="fa-brands fa-youtube"></i>'
        },
        {
            isim: "Dolap",
            url: "https://dolap.com/profil/hesabiniz",
            ikon: `<svg class="custom-svg-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                       <path d="M15 2 L5 12 L11 18 L15 14 V2 Z"></path>
                   </svg>`
        },
        {
            isim: "Gardrops",
            url: "https://gardrops.com/hesabiniz",
            ikon: `<svg class="custom-svg-icon" viewBox="0 0 24 24" fill="currentColor">
                       <text x="50%" y="78%" font-family="cursive, 'Brush Script MT', sans-serif" font-size="28" font-weight="bold" font-style="italic" text-anchor="middle">g</text>
                   </svg>`
        },
        {
            isim: "TikTok",
            url: "https://www.tiktok.com/tag/k%C4%B1rkyamal%C4%B1",
            ikon: '<i class="fa-brands fa-tiktok"></i>'
        },
        {
            isim: "Pinterest",
            url: "https://tr.pinterest.com/ferihapinarbasi/%C3%A7%C4%B1lg%C4%B1n-k%C4%B1rkyama/",
            ikon: '<i class="fa-brands fa-pinterest"></i>'
        }
    ]
};

let isUserLoggedIn = Boolean(window.SkimsApi ? SkimsApi.isAuthenticated() : localStorage.getItem("skims_jwt_token"));
let currentAuthUser = window.SkimsApi ? SkimsApi.getUser() : null;
let userFavoritesMap = {};
let errorTrackingInitialized = false;
let telemetryInitialized = false;
let pendingKvkkAction = null;

// Antigravity ekledi/değiştirdi: GÖREV 5 - KVKK / GDPR çerez onayı kontrol fonksiyonu
function hasKvkkConsent() {
    return localStorage.getItem("skims_kvkk_consent") === "accepted";
}

// Antigravity ekledi/değiştirdi: GÖREV 4 - Günlük, haftalık ve aylık raporlar için tarih (timestamp) bazlı veri kurgulama fonksiyonu
function olusturTarihBilesenleri() {
    const now = new Date();
    const pad = (n) => String(n).padStart(2, "0");
    const year = now.getFullYear();
    const month = pad(now.getMonth() + 1);
    const day = pad(now.getDate());
    const hour = pad(now.getHours());
    const minutes = pad(now.getMinutes());
    const seconds = pad(now.getSeconds());

    const tempDate = new Date(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()));
    const dayNum = tempDate.getUTCDay() || 7;
    tempDate.setUTCDate(tempDate.getUTCDate() + 4 - dayNum);
    const yearStart = new Date(Date.UTC(tempDate.getUTCFullYear(), 0, 1));
    const weekNo = Math.ceil((((tempDate - yearStart) / 86400000) + 1) / 7);

    return {
        timestamp: now.toISOString(),
        timestampMs: now.getTime(),
        isoDate: now.toISOString(),
        date: `${year}-${month}-${day}`,
        time: `${hour}:${minutes}:${seconds}`,
        dateKey: `${year}-${month}-${day}`,
        hourKey: `${year}-${month}-${day}_${hour}`,
        weekKey: `${year}-W${pad(weekNo)}`,
        monthKey: `${year}-${month}`,
        year: year
    };
}

// Antigravity ekledi/değiştirdi: GÖREV 4 - Tekil ziyaretçi takibi için Session ID oluşturma ve okuma fonksiyonu
function getOrCreateSessionId() {
    let sid = sessionStorage.getItem("skims_session_id");
    if (!sid) {
        sid = "sess_" + Date.now() + "_" + Math.random().toString(36).substring(2, 10);
        sessionStorage.setItem("skims_session_id", sid);
    }
    return sid;
}

// Antigravity ekledi/değiştirdi: GÖREV 3 - Yerel log geçmişine (localStorage) hata kaydetme fonksiyonu (KVKK onayı olmadan çalışmaz)
function kaydetHataLogu(errorType, errorDetails) {
    if (!hasKvkkConsent()) return;

    const zaman = olusturTarihBilesenleri();
    const logPayload = {
        errorType: errorType,
        message: String(errorDetails.message || "Bilinmeyen Hata"),
        source: String(errorDetails.source || window.location.href),
        lineno: errorDetails.lineno || null,
        colno: errorDetails.colno || null,
        stack: errorDetails.stack ? String(errorDetails.stack).slice(0, 1500) : null,
        date: zaman.date,
        time: zaman.time,
        timestamp: zaman.timestamp,
        timestampMs: zaman.timestampMs,
        dateKey: zaman.dateKey,
        monthKey: zaman.monthKey,
        pageUrl: window.location.href,
        sessionId: getOrCreateSessionId(),
        userId: currentAuthUser ? currentAuthUser.uid : "anonymous",
        browserInfo: {
            userAgent: navigator.userAgent,
            language: navigator.language || "",
            platform: navigator.platform || "",
            screen: `${window.screen.width}x${window.screen.height}`,
            viewport: `${window.innerWidth}x${window.innerHeight}`
        }
    };

    try {
        const mevcutHatalar = JSON.parse(localStorage.getItem("skims_local_logs_errors") || "[]");
        mevcutHatalar.unshift({ ...logPayload, timestamp: zaman.isoDate });
        localStorage.setItem("skims_local_logs_errors", JSON.stringify(mevcutHatalar.slice(0, 200)));
    } catch (_) {}
}

// Antigravity ekledi/değiştirdi: GÖREV 3 - Global hata yakalayıcı (window.onerror, sayfa yükleme hataları ve console.error dinleyicisi)
function baslatHataTakibi() {
    if (!hasKvkkConsent() || errorTrackingInitialized) return;
    errorTrackingInitialized = true;

    const previousOnError = window.onerror;
    window.onerror = function(message, source, lineno, colno, errorObj) {
        kaydetHataLogu("runtime_error", {
            message: message,
            source: source,
            lineno: lineno,
            colno: colno,
            stack: errorObj && errorObj.stack ? errorObj.stack : null
        });
        if (typeof previousOnError === "function") {
            return previousOnError.apply(this, arguments);
        }
        return false;
    };

    window.addEventListener("error", (event) => {
        if (event.target && event.target !== window) {
            const tagName = event.target.tagName || "RESOURCE";
            const resourceUrl = event.target.src || event.target.href || "";
            kaydetHataLogu("page_resource_load_error", {
                message: `${tagName} yükleme hatası: ${resourceUrl}`,
                source: resourceUrl
            });
        }
    }, true);

    window.addEventListener("unhandledrejection", (event) => {
        const reason = event.reason;
        kaydetHataLogu("unhandled_promise_rejection", {
            message: reason && reason.message ? reason.message : String(reason),
            stack: reason && reason.stack ? reason.stack : null
        });
    });

    const originalConsoleError = console.error;
    let isLoggingConsoleError = false;
    console.error = function(...args) {
        originalConsoleError.apply(console, args);
        if (isLoggingConsoleError || !hasKvkkConsent()) return;
        isLoggingConsoleError = true;
        try {
            const msg = args.map(a => {
                if (a instanceof Error) return `${a.message}\n${a.stack || ""}`;
                if (typeof a === "object") {
                    try { return JSON.stringify(a); } catch (_) { return String(a); }
                }
                return String(a);
            }).join(" | ");
            kaydetHataLogu("console_error", {
                message: msg,
                source: window.location.href
            });
        } finally {
            isLoggingConsoleError = false;
        }
    };
}

// Antigravity ekledi/değiştirdi: GÖREV 4 - Özel JS telemetri fonksiyonu (yerel analitik log geçmişine gönderir)
function gonderTelemetriVerisi(eventCategory, eventAction, eventMeta = {}) {
    if (!hasKvkkConsent()) return;

    const zaman = olusturTarihBilesenleri();
    const analyticsPayload = {
        eventCategory: eventCategory,
        eventAction: eventAction,
        metadata: eventMeta,
        sessionId: getOrCreateSessionId(),
        userId: currentAuthUser ? currentAuthUser.uid : "anonymous",
        pageUrl: window.location.href,
        pagePath: window.location.pathname,
        timestamp: zaman.timestamp,
        timestampMs: zaman.timestampMs,
        isoDate: zaman.isoDate,
        dateKey: zaman.dateKey,
        hourKey: zaman.hourKey,
        weekKey: zaman.weekKey,
        monthKey: zaman.monthKey,
        year: zaman.year
    };

    try {
        const mevcutAnalitikler = JSON.parse(localStorage.getItem("skims_local_logs_analytics") || "[]");
        mevcutAnalitikler.unshift({ ...analyticsPayload, timestamp: zaman.isoDate });
        localStorage.setItem("skims_local_logs_analytics", JSON.stringify(mevcutAnalitikler.slice(0, 300)));
    } catch (_) {}
}
window.gonderTelemetriVerisi = gonderTelemetriVerisi;

// Antigravity ekledi/değiştirdi: Site yayına alınmadan da tarayıcı konsolundan log geçmişini tablo halinde görüntülemeyi sağlayan yardımcı fonksiyon
window.logGecmisiniGoruntule = function() {
    const analitikLoglar = JSON.parse(localStorage.getItem("skims_local_logs_analytics") || "[]");
    const hataLoglar = JSON.parse(localStorage.getItem("skims_local_logs_errors") || "[]");
    console.group("📊 SKIMS Telemetri & Analitik Log Geçmişi (" + analitikLoglar.length + " kayıt)");
    console.table(analitikLoglar.map(k => ({
        Tarih: k.isoDate,
        Kategori: k.eventCategory,
        Islem: k.eventAction,
        Detay: JSON.stringify(k.metadata),
        Sayfa: k.pagePath,
        Kullanici: k.userId
    })));
    console.groupEnd();
    console.group("⚠️ SKIMS Hata (Error) Log Geçmişi (" + hataLoglar.length + " kayıt)");
    console.table(hataLoglar.map(h => ({
        Tarih: h.date + " " + h.time,
        Tip: h.errorType,
        Mesaj: h.message,
        Kaynak: h.source
    })));
    console.groupEnd();
    return { analytics: analitikLoglar, errors: hataLoglar };
};

// Antigravity ekledi/değiştirdi: GÖREV 4 - Günlük/saatlik tekil ziyaretçi loglama ve kullanıcı etkileşim telemetrisini başlatma
function baslatTelemetri() {
    if (!hasKvkkConsent() || telemetryInitialized) return;
    telemetryInitialized = true;

    const zaman = olusturTarihBilesenleri();
    const sessionId = getOrCreateSessionId();
    const lastLoggedHour = sessionStorage.getItem("skims_logged_hour_key");
    const lastLoggedDate = localStorage.getItem("skims_logged_date_key");

    const isHourlyUnique = lastLoggedHour !== zaman.hourKey;
    const isDailyUnique = lastLoggedDate !== zaman.dateKey;

    if (isHourlyUnique || isDailyUnique) {
        sessionStorage.setItem("skims_logged_hour_key", zaman.hourKey);
        localStorage.setItem("skims_logged_date_key", zaman.dateKey);
        gonderTelemetriVerisi("visitor", "unique_visit", {
            sessionId: sessionId,
            isDailyUnique: isDailyUnique,
            isHourlyUnique: isHourlyUnique,
            referrer: document.referrer || "direct"
        });
    }
}

// Antigravity ekledi/değiştirdi: GÖREV 5 - KVKK ve Çerez Onay Banner'ı ile Zorunlu Modal yönetimi fonksiyonları
function kvkkOnayiniKabulEt() {
    localStorage.setItem("skims_kvkk_consent", "accepted");
    const bannerEl = document.getElementById("kvkk-banner");
    const modalEl = document.getElementById("kvkk-modal-backdrop");
    if (bannerEl) bannerEl.classList.add("hidden");
    if (modalEl) modalEl.classList.remove("active");

    baslatHataTakibi();
    baslatTelemetri();

    if (typeof pendingKvkkAction === "function") {
        const cb = pendingKvkkAction;
        pendingKvkkAction = null;
        cb();
    }
}

// Antigravity ekledi/değiştirdi: GÖREV 5 - İstisna durumları (Giriş Yap veya Favorilere Ekle) için zorunlu KVKK modalı gösterme
function zorunluKvkkModalGoster(onaySonrasiCallback) {
    pendingKvkkAction = onaySonrasiCallback || null;
    const modalEl = document.getElementById("kvkk-modal-backdrop");
    if (modalEl) {
        modalEl.classList.add("active");
    }
}

// Antigravity ekledi/değiştirdi: GÖREV 5 - Sayfa altına KVKK/GDPR onay bandı ve zorunlu modal DOM elemanlarını ekleme
function olusturKvkkArayuzu() {
    if (document.getElementById("kvkk-banner")) return;

    const banner = document.createElement("div");
    banner.id = "kvkk-banner";
    banner.className = hasKvkkConsent() ? "kvkk-banner hidden" : "kvkk-banner";
    banner.innerHTML = `
        <div class="kvkk-banner-text">
            <strong>ÇEREZ VE KVKK AYDINLATMA METNİ</strong>
            Alışveriş deneyiminizi iyileştirmek, hata takibi ve analitik/telemetri çalışmalarını yürütebilmek için çerezler kullanıyoruz. Onay vermeden hiçbir telemetri veya hata loglama işlemi başlatılmaz.
        </div>
        <div class="kvkk-banner-actions">
            <button type="button" class="kvkk-btn kvkk-btn-reject" id="kvkk-reject-btn">Daha Sonra</button>
            <button type="button" class="kvkk-btn kvkk-btn-accept" id="kvkk-accept-btn">Kabul Et</button>
        </div>
    `;
    document.body.appendChild(banner);

    const modalBackdrop = document.createElement("div");
    modalBackdrop.id = "kvkk-modal-backdrop";
    modalBackdrop.className = "kvkk-modal-backdrop";
    modalBackdrop.innerHTML = `
        <div class="kvkk-modal-box">
            <h3 class="kvkk-modal-title">KVKK VE ÇEREZ ONAYI GEREKLİ</h3>
            <p class="kvkk-modal-message">Bu işlemleri yapabilmek için çerez ve KVKK koşullarını kabul etmelisiniz</p>
            <div class="kvkk-modal-actions">
                <button type="button" class="kvkk-btn-close" id="kvkk-modal-cancel-btn">Vazgeç</button>
                <button type="button" class="kvkk-btn kvkk-btn-accept" id="kvkk-modal-accept-btn">Kabul Et ve Devam Et</button>
            </div>
        </div>
    `;
    document.body.appendChild(modalBackdrop);

    const acceptBtn = document.getElementById("kvkk-accept-btn");
    const rejectBtn = document.getElementById("kvkk-reject-btn");
    const modalAcceptBtn = document.getElementById("kvkk-modal-accept-btn");
    const modalCancelBtn = document.getElementById("kvkk-modal-cancel-btn");

    if (acceptBtn) acceptBtn.addEventListener("click", kvkkOnayiniKabulEt);
    if (modalAcceptBtn) modalAcceptBtn.addEventListener("click", kvkkOnayiniKabulEt);
    if (rejectBtn) {
        rejectBtn.addEventListener("click", () => {
            banner.classList.add("hidden");
        });
    }
    if (modalCancelBtn) {
        modalCancelBtn.addEventListener("click", () => {
            pendingKvkkAction = null;
            modalBackdrop.classList.remove("active");
        });
    }

    if (hasKvkkConsent()) {
        baslatHataTakibi();
        baslatTelemetri();
    }
}

// Kullanıcının favori ürünlerini .NET 10 Web API (GET /api/favorites + JWT) üzerinden çekip kalp ikonlarını senkronize etme
async function kullaniciFavorileriniYukle(uid) {
    const aktifUid = uid || (currentAuthUser && currentAuthUser.uid) || "default_user";
    userFavoritesMap = {};
    try {
        const genelFavs = localStorage.getItem("skims_favorites_data");
        const kullaniciFavs = localStorage.getItem(`skims_favorites_${aktifUid}`);
        if (genelFavs) {
            userFavoritesMap = { ...userFavoritesMap, ...(JSON.parse(genelFavs) || {}) };
        }
        if (kullaniciFavs) {
            userFavoritesMap = { ...userFavoritesMap, ...(JSON.parse(kullaniciFavs) || {}) };
        }
    } catch (_) {}

    senkronizeKalpIkonlari();

    if (window.SkimsApi && SkimsApi.isAuthenticated()) {
        try {
            const apiFavs = await SkimsApi.getFavorites();
            // Yerel olarak saklanan kolaj (sayısal olmayan ID) favorilerini koru
            const yerelStatikFavs = {};
            Object.entries(userFavoritesMap).forEach(([k, v]) => {
                if (!/^\d+$/.test(String(k))) {
                    yerelStatikFavs[k] = v;
                }
            });

            const map = { ...yerelStatikFavs };
            apiFavs.forEach(item => {
                const pid = String(item.productId);
                map[pid] = {
                    productId: pid,
                    marka: item.brand,
                    baslik: item.title,
                    fiyat: SkimsApi.formatPrice(item.price),
                    indirim: item.discountLabel || "%0",
                    resim: item.imageUrl,
                    link: item.productLink || "#"
                };
            });
            userFavoritesMap = map;
            localStorage.setItem("skims_favorites_data", JSON.stringify(userFavoritesMap));
            localStorage.setItem(`skims_favorites_${aktifUid}`, JSON.stringify(userFavoritesMap));
            senkronizeKalpIkonlari();
            if (typeof window.renderFavoriSayfasiKolajlari === "function") {
                window.renderFavoriSayfasiKolajlari();
            }
        } catch (err) {
            console.warn("Favoriler API'den senkronize edilemedi:", err.message);
        }
    }
}

// Sayfadaki tüm ürün kartlarının kalp ikonlarını favori durumuna göre "dolu/kırmızı (liked)" olarak güncelleme
function senkronizeKalpIkonlari() {
    const allHearts = document.querySelectorAll(".heart-btn[data-product-id]");
    allHearts.forEach(btn => {
        const pid = btn.getAttribute("data-product-id");
        if (isUserLoggedIn && pid && userFavoritesMap[pid]) {
            btn.classList.add("liked");
            btn.innerHTML = "&#9829;";
        } else {
            btn.classList.remove("liked");
            btn.innerHTML = "&#9825;";
        }
    });
}

// Kalp butonuna basılınca .NET 10 Web API (POST /api/favorites/toggle/{productId} + JWT) ile veritabanını güncelleme
async function favoriDurumuDegistir(btnElement, urunVerisi) {
    if (!isUserLoggedIn) return;
    const uid = (currentAuthUser && currentAuthUser.uid) ? currentAuthUser.uid : "default_user";

    const cardEl = btnElement.closest(".product-card, .card");
    const productId = String(urunVerisi.productId || btnElement.getAttribute("data-product-id") || ("urun_" + Date.now()));
    const marka = urunVerisi.marka || (cardEl && cardEl.querySelector(".brand, .brand-name") ? cardEl.querySelector(".brand, .brand-name").textContent.trim() : "");
    const baslik = urunVerisi.baslik || (cardEl && cardEl.querySelector(".title, .product-title") ? cardEl.querySelector(".title, .product-title").textContent.trim() : "");
    const fiyat = urunVerisi.fiyat || (cardEl && cardEl.querySelector(".price") ? cardEl.querySelector(".price").textContent.trim() : "");
    const indirim = urunVerisi.indirim || (cardEl && cardEl.querySelector(".discount") ? cardEl.querySelector(".discount").textContent.trim() : "%0");
    const resim = urunVerisi.resim || (cardEl && cardEl.querySelector("img") ? cardEl.querySelector("img").getAttribute("src") : "");
    const link = urunVerisi.link || (cardEl && cardEl.querySelector("a") ? cardEl.querySelector("a").getAttribute("href") : "#");

    // Optimistic UI güncellemesi (sadece renk/ikon durumu değişir, boyut/konum değişmez)
    btnElement.classList.toggle("liked");
    let kalpKirmiziMi = btnElement.classList.contains("liked");
    btnElement.innerHTML = kalpKirmiziMi ? "&#9829;" : "&#9825;";

    // Veritabanı kaynaklı ürünse (sayısal ID) ASP.NET Core API'ye JWT ile gönder
    if (window.SkimsApi && SkimsApi.isAuthenticated() && /^\d+$/.test(productId)) {
        try {
            const apiRes = await SkimsApi.toggleFavorite(Number(productId));
            kalpKirmiziMi = Boolean(apiRes.isFavorited);
            btnElement.classList.toggle("liked", kalpKirmiziMi);
            btnElement.innerHTML = kalpKirmiziMi ? "&#9829;" : "&#9825;";
        } catch (err) {
            if (err.status === 401) {
                isUserLoggedIn = false;
                currentAuthUser = null;
                guncelleProfilMenusu();
                kaydetOncekiSayfaVeGiriseGit();
                return;
            }
            console.warn("Favori API güncelleme uyarısı:", err.message);
        }
    }

    if (kalpKirmiziMi) {
        userFavoritesMap[productId] = {
            productId: productId,
            marka: marka,
            baslik: baslik,
            fiyat: fiyat,
            indirim: indirim,
            resim: resim,
            link: link,
            addedAt: new Date().toISOString()
        };
    } else {
        delete userFavoritesMap[productId];
    }

    localStorage.setItem("skims_favorites_data", JSON.stringify(userFavoritesMap));
    localStorage.setItem(`skims_favorites_${uid}`, JSON.stringify(userFavoritesMap));

    if (typeof window.renderFavoriSayfasiKolajlari === "function") {
        window.renderFavoriSayfasiKolajlari();
    }
}

// Dinamik kolajlardaki (Bölüm B, C, D) kalp butonlarının gerçek favorileme/KVKK/oturum akışını kullanması için global köprü
window.SkimsFavoritesBridge = {
    syncHearts: () => senkronizeKalpIkonlari(),
    handleHeartClick: (btnElement, urunVerisi) => {
        if (!isUserLoggedIn) {
            if (!hasKvkkConsent()) {
                zorunluKvkkModalGoster(() => { kaydetOncekiSayfaVeGiriseGit(); });
            } else {
                kaydetOncekiSayfaVeGiriseGit();
            }
            return;
        }
        if (!hasKvkkConsent()) {
            zorunluKvkkModalGoster(() => { favoriDurumuDegistir(btnElement, urunVerisi); });
            return;
        }
        favoriDurumuDegistir(btnElement, urunVerisi);
    }
};

// Giriş sayfasına yönlendirmeden önce kullanıcının bulunduğu sayfayı kaydeden ve giriş sonrası önceki sayfaya döndüren fonksiyonlar
function kaydetOncekiSayfaVeGiriseGit() {
    if (!window.location.href.includes("giris_yap.html")) {
        localStorage.setItem("skims_return_url", window.location.href);
        sessionStorage.setItem("skims_return_url", window.location.href);
    }
    window.location.href = "giris_yap.html";
}

function oncekiSayfayaGeriDon() {
    const kayitliUrl = localStorage.getItem("skims_return_url") || sessionStorage.getItem("skims_return_url");
    localStorage.removeItem("skims_return_url");
    sessionStorage.removeItem("skims_return_url");

    if (kayitliUrl && !kayitliUrl.includes("giris_yap.html")) {
        window.location.href = kayitliUrl;
    } else if (document.referrer && !document.referrer.includes("giris_yap.html")) {
        window.location.href = document.referrer;
    } else {
        window.location.href = "index.html";
    }
}

// Çıkış yapma (JWT oturum temizleme)
async function kullaniciCikisYap(e) {
    if (e) e.preventDefault();
    if (window.SkimsApi) {
        SkimsApi.clearSession();
    } else {
        localStorage.removeItem("skims_jwt_token");
        localStorage.removeItem("skims_auth_user");
        sessionStorage.removeItem("skims_auth_user");
    }
    currentAuthUser = null;
    isUserLoggedIn = false;
    userFavoritesMap = {};
    guncelleProfilMenusu();
    senkronizeKalpIkonlari();
    if (typeof window.renderFavoriSayfasiKolajlari === "function") {
        window.renderFavoriSayfasiKolajlari();
    }
}

// Üst menüdeki "Profil" dropdown menüsü: JWT oturum ve rol durumuna göre dinamik güncellenir
function guncelleProfilMenusu() {
    const profileDropdown = document.getElementById("profile-dropdown");
    if (!profileDropdown) return;

    if (window.SkimsApi) {
        isUserLoggedIn = SkimsApi.isAuthenticated();
        currentAuthUser = SkimsApi.getUser();
    }

    if (isUserLoggedIn && currentAuthUser) {
        const adminLinkHTML = (window.SkimsApi && SkimsApi.isAdmin())
            ? `<a href="admin.html" class="dropdown-item" id="admin-action-link">Admin Paneli</a>`
            : "";

        profileDropdown.innerHTML = `
            <a href="favori.html" class="dropdown-item" id="profile-action-link">Favorilerim (${currentAuthUser.fullName || currentAuthUser.displayName || "Hesabım"})</a>
            ${adminLinkHTML}
            <a href="#" class="dropdown-item" id="logout-action-link">Çıkış Yap</a>
        `;
        const logoutLink = document.getElementById("logout-action-link");
        if (logoutLink) {
            logoutLink.addEventListener("click", kullaniciCikisYap);
        }
    } else {
        profileDropdown.innerHTML = `
            <a href="giris_yap.html" class="dropdown-item" id="login-action-link">Giriş Yap / Kayıt Ol</a>
        `;
        const loginLink = document.getElementById("login-action-link");
        if (loginLink) {
            loginLink.addEventListener("click", (e) => {
                e.preventDefault();
                if (!hasKvkkConsent()) {
                    zorunluKvkkModalGoster(() => {
                        kaydetOncekiSayfaVeGiriseGit();
                    });
                    return;
                }
                kaydetOncekiSayfaVeGiriseGit();
            });
        }
    }

    // Mobil menü çekmecesi alt hesap alanını da senkronize et
    const mobileAccountFooter = document.getElementById("mobile-menu-account-footer");
    if (mobileAccountFooter) {
        if (isUserLoggedIn && currentAuthUser) {
            const adminMobileBtn = (window.SkimsApi && SkimsApi.isAdmin())
                ? `<a href="admin.html" class="mobile-drawer-account-btn"><i class="fa-solid fa-sliders"></i><span>Admin Paneli</span></a>`
                : "";
            mobileAccountFooter.innerHTML = `
                <a href="favori.html" class="mobile-drawer-account-btn"><i class="fa-regular fa-heart"></i><span>Favorilerim (${currentAuthUser.fullName || currentAuthUser.displayName || "Hesabım"})</span></a>
                ${adminMobileBtn}
                <button type="button" class="mobile-drawer-account-btn mobile-drawer-logout" id="mobile-logout-action-btn"><i class="fa-solid fa-right-from-bracket"></i><span>Çıkış Yap</span></button>
            `;
            const mobileLogoutBtn = document.getElementById("mobile-logout-action-btn");
            if (mobileLogoutBtn) {
                mobileLogoutBtn.addEventListener("click", kullaniciCikisYap);
            }
        } else {
            mobileAccountFooter.innerHTML = `
                <a href="giris_yap.html" class="mobile-drawer-account-btn mobile-drawer-login-cta" id="mobile-login-action-link"><i class="fa-regular fa-user"></i><span>Giriş Yap / Kayıt Ol</span></a>
            `;
            const mobileLoginLink = document.getElementById("mobile-login-action-link");
            if (mobileLoginLink) {
                mobileLoginLink.addEventListener("click", (e) => {
                    e.preventDefault();
                    if (!hasKvkkConsent()) {
                        zorunluKvkkModalGoster(() => {
                            kaydetOncekiSayfaVeGiriseGit();
                        });
                        return;
                    }
                    kaydetOncekiSayfaVeGiriseGit();
                });
            }
        }
    }
}

document.addEventListener("DOMContentLoaded", () => {
    // Sayfa modunu belirle: ?kategori= varsa alt sayfa (page-category), yoksa ana sayfa (page-home)
    const activeSlug = (window.SkimsPageSections && typeof SkimsPageSections.extractCategorySlug === "function")
        ? SkimsPageSections.extractCategorySlug(window.location.search)
        : (new URLSearchParams(window.location.search).get("kategori") || "").trim().toLowerCase();
    const isCategoryPage = Boolean(activeSlug);

    document.body.classList.toggle("page-category", isCategoryPage);
    document.body.classList.toggle("page-home", !isCategoryPage);

    if (window.location.href.includes("giris_yap.html") && !sessionStorage.getItem("skims_return_url")) {
        if (document.referrer && !document.referrer.includes("giris_yap.html")) {
            sessionStorage.setItem("skims_return_url", document.referrer);
        }
    }

    olusturKvkkArayuzu();

    // JWT oturumunu doğrula ve favorileri API'den senkronize et
    if (window.SkimsApi && SkimsApi.isAuthenticated()) {
        currentAuthUser = SkimsApi.getUser();
        isUserLoggedIn = true;
        SkimsApi.me()
            .then(user => {
                const updated = {
                    id: user.id,
                    uid: String(user.id),
                    fullName: user.fullName,
                    displayName: user.fullName,
                    email: user.email,
                    role: user.role
                };
                localStorage.setItem("skims_auth_user", JSON.stringify(updated));
                currentAuthUser = updated;
                isUserLoggedIn = true;
                guncelleProfilMenusu();
                kullaniciFavorileriniYukle(updated.uid);
            })
            .catch(err => {
                if (err.status === 401) {
                    isUserLoggedIn = false;
                    currentAuthUser = null;
                    guncelleProfilMenusu();
                    senkronizeKalpIkonlari();
                } else {
                    kullaniciFavorileriniYukle(currentAuthUser.uid);
                }
            });
    }

    // ========================================================
    // 0. ÜST BÖLÜM (LOGO VE MENÜ İSKELETİ) OLUŞTURMA
    // ========================================================
    const headerTopEl = document.getElementById("header-logo-container");
    if (headerTopEl) {
        headerTopEl.innerHTML = `
          <div class="logo">
            <a href="${SITE_CHROME_DEFAULTS.logo.link}">
                <img src="${SITE_CHROME_DEFAULTS.logo.src}" alt="${SITE_CHROME_DEFAULTS.logo.alt}">
            </a>
          </div>
        `;
    }

    const headerNavEl = document.getElementById("header-nav-container");
    if (headerNavEl) {
        headerNavEl.innerHTML = `
          <div class="katalog-wrapper">
            <button class="katalog-btn" aria-label="Katalog Menüsü">
              <svg width="18" height="14" viewBox="0 0 16 12" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M1 1H15 M1 6H15 M1 11H15" stroke="#111" stroke-width="1.7" stroke-linecap="round"/>
              </svg>
            </button>
            <div class="mega-menu">
              <div class="mega-sidebar"></div>
              <div class="mega-content-area"></div>
            </div>
          </div>

          <div class="main-horizontal-nav"></div>

          <!-- MOBİL ORTA LOGO (Yalnızca <= 768px ekranlarda görünür) -->
          <div class="mobile-header-logo">
            <a href="${SITE_CHROME_DEFAULTS.logo.link}" aria-label="Ana Sayfa">
              <img src="${SITE_CHROME_DEFAULTS.logo.src}" alt="${SITE_CHROME_DEFAULTS.logo.alt}">
            </a>
          </div>

          <!-- HEADER İKONLARI (Arama, Profil, Favori) -->
          <div class="header-actions">
            <button class="action-btn" id="search-btn" aria-label="Ara">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><circle cx="11" cy="11" r="8"></circle><line x1="21" y1="21" x2="16.65" y2="16.65"></line></svg>
            </button>

            <div class="profile-wrapper">
              <button class="action-btn" id="profile-btn" aria-label="Profil">
                <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg>
              </button>
              <div class="profile-dropdown" id="profile-dropdown">
                 <!-- Duruma göre JS ile doldurulacak -->
              </div>
            </div>

            <button class="action-btn" id="fav-btn" aria-label="Favoriler">
              <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"><path d="M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 0 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z"></path></svg>
            </button>
          </div>
        `;

        // Profil menüsü JWT oturum durumuna göre dinamik güncelleniyor
        guncelleProfilMenusu();

        // --- MOBİL HAMBURGER MENÜ ÇEKMECESİ (<= 1024px) VE DOKUNMATİK PROFİL MENÜSÜ ---
        const mobileDrawerBackdrop = document.createElement("div");
        mobileDrawerBackdrop.id = "mobile-menu-backdrop";
        mobileDrawerBackdrop.className = "mobile-menu-backdrop";

        const mobileDrawer = document.createElement("aside");
        mobileDrawer.id = "mobile-menu-drawer";
        mobileDrawer.className = "mobile-menu-drawer";
        mobileDrawer.setAttribute("aria-hidden", "true");
        mobileDrawer.innerHTML = `
            <div class="mobile-drawer-header">
                <span class="mobile-drawer-title">MENÜ</span>
                <button type="button" class="mobile-drawer-close-btn" id="mobile-drawer-close-btn" aria-label="Menüyü Kapat">
                    <i class="fa-solid fa-xmark"></i>
                </button>
            </div>
            <div class="mobile-drawer-body">
                <a href="index.html" class="mobile-drawer-home-link">
                    <span>ANA SAYFA</span>
                    <i class="fa-solid fa-chevron-right"></i>
                </a>
                <div id="mobile-menu-accordion-root" class="mobile-menu-accordion-root"></div>
            </div>
            <div class="mobile-drawer-footer" id="mobile-menu-account-footer"></div>
        `;
        document.body.appendChild(mobileDrawerBackdrop);
        document.body.appendChild(mobileDrawer);

        // Hesap alt alanını da ilk çizimde doldur
        guncelleProfilMenusu();

        const openMobileDrawer = () => {
            mobileDrawer.classList.add("is-open");
            mobileDrawerBackdrop.classList.add("is-open");
            mobileDrawer.setAttribute("aria-hidden", "false");
            document.body.classList.add("mobile-drawer-open");
        };
        const closeMobileDrawer = () => {
            mobileDrawer.classList.remove("is-open");
            mobileDrawerBackdrop.classList.remove("is-open");
            mobileDrawer.setAttribute("aria-hidden", "true");
            document.body.classList.remove("mobile-drawer-open");
        };
        window.SkimsCloseMobileDrawer = closeMobileDrawer;

        const katalogBtn = headerNavEl.querySelector(".katalog-btn");
        if (katalogBtn) {
            katalogBtn.addEventListener("click", (e) => {
                if (window.innerWidth <= 1024) {
                    e.preventDefault();
                    e.stopPropagation();
                    if (mobileDrawer.classList.contains("is-open")) {
                        closeMobileDrawer();
                    } else {
                        openMobileDrawer();
                    }
                }
            });
        }

        const mobileCloseBtn = document.getElementById("mobile-drawer-close-btn");
        if (mobileCloseBtn) mobileCloseBtn.addEventListener("click", closeMobileDrawer);
        mobileDrawerBackdrop.addEventListener("click", closeMobileDrawer);
        document.addEventListener("keydown", (e) => {
            if (e.key === "Escape" && mobileDrawer.classList.contains("is-open")) {
                closeMobileDrawer();
            }
        });
        window.addEventListener("resize", () => {
            if (window.innerWidth > 1024 && mobileDrawer.classList.contains("is-open")) {
                closeMobileDrawer();
            }
        });

        // Dokunmatik ekranlarda Profil dropdown açma/kapama
        const profileWrapper = headerNavEl.querySelector(".profile-wrapper");
        const profileBtnEl = document.getElementById("profile-btn");
        if (profileWrapper && profileBtnEl) {
            profileBtnEl.addEventListener("click", (e) => {
                if (window.innerWidth <= 1024) {
                    e.preventDefault();
                    e.stopPropagation();
                    profileWrapper.classList.toggle("is-open");
                }
            });
            document.addEventListener("click", (e) => {
                if (profileWrapper.classList.contains("is-open") && !profileWrapper.contains(e.target)) {
                    profileWrapper.classList.remove("is-open");
                }
            });
        }

        // Mevcut sayfa bir kategori sayfasıysa (?kategori=slug) görüntülenme logunu kaydet
        if (activeSlug) {
            gonderTelemetriVerisi("mega_menu", "category_page_view", {
                categoryName: activeSlug,
                targetUrl: `index.html?kategori=${encodeURIComponent(activeSlug)}`
            });
        }

        // --- FAVORİLER BUTONU KONTROLÜ ---
        // Antigravity ekledi/değiştirdi: GÖREV 2 & GÖREV 5 - Üst menüdeki favori butonu giriş yapıldıysa favori.html'ye, yapılmadıysa mevcut sayfayı kaydedip giris_yap.html'ye yönlendirir
        const favBtn = document.getElementById("fav-btn");
        if (favBtn) {
            favBtn.addEventListener("click", (e) => {
                e.preventDefault(); // Sayfanın en üste zıplamasını engeller
                const yonlendirFavoriVeyaGiris = () => {
                    if (isUserLoggedIn) {
                        window.location.href = "favori.html";
                    } else {
                        kaydetOncekiSayfaVeGiriseGit();
                    }
                };
                if (!hasKvkkConsent()) {
                    zorunluKvkkModalGoster(yonlendirFavoriVeyaGiris);
                    return;
                }
                yonlendirFavoriVeyaGiris();
            });
        }

        // --- ARAMA (SEARCH) PENCERESİ OLUŞTURMA VE KONTROLÜ ---
        const searchOverlay = document.createElement("div");
        searchOverlay.className = "search-overlay";
        searchOverlay.id = "search-overlay";
        searchOverlay.innerHTML = `
            <div class="search-top-bar">
                <div class="search-input-wrapper">
                    <!-- Otomatik tamamlamayı kesin kapatmak için autocomplete="new-password" ve spellcheck="false" eklendi -->
                    <input type="text" id="search-input" autocomplete="new-password" spellcheck="false" placeholder="">
                    <span class="search-submit-icon"><i class="fa-solid fa-magnifying-glass"></i></span>
                </div>
                <button class="search-close-btn" id="search-close-btn"><i class="fa-solid fa-xmark"></i></button>
            </div>
            <div class="search-content" id="search-content">
                <h4 class="search-heading">POPULAR SEARCHES</h4>
                <ul class="search-results-list" id="search-results-list">
                </ul>
            </div>
        `;
        document.body.appendChild(searchOverlay);

        const searchBtn = document.getElementById("search-btn");
        const searchCloseBtn = document.getElementById("search-close-btn");
        const searchInput = document.getElementById("search-input");
        const resultsList = document.getElementById("search-results-list");
        const searchHeading = document.querySelector(".search-heading");

        const defaultPopularSearches = ["Bag", "Matin Kim", "Wool Coat", "Funnel Neck", "Nothing Written"];

        function loadDefaultSearches() {
            searchHeading.textContent = "POPULAR SEARCHES";
            resultsList.innerHTML = "";
            defaultPopularSearches.forEach(item => {
                const li = document.createElement("li");
                li.textContent = item;
                // Antigravity ekledi/değiştirdi: GÖREV 4 - Popüler arama kelimesine tıklandığında telemetri logu gönderilmesi
                li.addEventListener("click", () => {
                    searchInput.value = item;
                    gonderTelemetriVerisi("search", "search_keyword", {
                        keyword: item.toLowerCase(),
                        source: "popular_searches"
                    });
                });
                resultsList.appendChild(li);
            });
        }
        loadDefaultSearches();

        if (searchBtn) {
            searchBtn.addEventListener("click", () => {
                searchOverlay.classList.add("active");
                searchInput.value = "";
                loadDefaultSearches();
                document.body.style.overflow = "hidden";
                setTimeout(() => searchInput.focus(), 400);
            });
        }
        searchCloseBtn.addEventListener("click", () => {
            searchOverlay.classList.remove("active");
            document.body.style.overflow = "";
        });

        function getPageWords() {
            const text = document.body.innerText || "";
            const words = text.match(/[a-zA-ZçğıöşüÇĞİÖŞÜ]{3,}/g) || [];
            return [...new Set(words.map(w => w.toLowerCase()))];
        }

        // Antigravity ekledi/değiştirdi: GÖREV 4 - Arama çubuğuna yazılan anahtar kelimeleri gecikmeli (debounce) ve Enter tuşu ile kaydetme
        let searchTelemetryTimeout = null;
        searchInput.addEventListener("keydown", (e) => {
            if (e.key === "Enter") {
                const keyword = e.target.value.toLowerCase().trim();
                if (keyword.length >= 2) {
                    clearTimeout(searchTelemetryTimeout);
                    gonderTelemetriVerisi("search", "search_keyword", {
                        keyword: keyword,
                        source: "enter_key"
                    });
                }
            }
        });

        searchInput.addEventListener("input", (e) => {
            const query = e.target.value.toLowerCase().trim();
            const pageWords = getPageWords();
            resultsList.innerHTML = "";

            // Antigravity ekledi/değiştirdi: GÖREV 4 - Kullanıcı arama yazmayı bitirdiğinde (700ms debounce) anahtar kelimeyi logla
            clearTimeout(searchTelemetryTimeout);
            if (query.length >= 2) {
                searchTelemetryTimeout = setTimeout(() => {
                    gonderTelemetriVerisi("search", "search_keyword", {
                        keyword: query,
                        source: "input_typing"
                    });
                }, 700);
            }

            if (query === "") {
                loadDefaultSearches();
            } else {
                searchHeading.textContent = "ÖNERİLENLER";
                const matchedWords = pageWords.filter(word => word.includes(query)).slice(0, 8);

                if (matchedWords.length > 0) {
                    matchedWords.forEach(item => {
                        const li = document.createElement("li");
                        li.textContent = item;
                        // Antigravity ekledi/değiştirdi: GÖREV 4 - Önerilen arama kelimesine tıklandığında telemetri logu gönder
                        li.addEventListener("click", () => {
                            searchInput.value = item;
                            gonderTelemetriVerisi("search", "search_keyword", {
                                keyword: item,
                                source: "suggested_result"
                            });
                        });
                        resultsList.appendChild(li);
                    });
                } else {
                    const li = document.createElement("li");
                    li.textContent = "Sayfada benzer sonuç bulunamadı...";
                    li.style.color = "#999";
                    li.style.cursor = "default";
                    resultsList.appendChild(li);
                }
            }
        });
    }

    // ========================================================
    // 1. BÖLÜM ALT: FOOTER KONTROLÜ (HEADER İLE BİRLİKTE HAZIRLA)
    // ========================================================
    const yearElement = document.getElementById("current-year");
    if (yearElement) {
        yearElement.textContent = new Date().getFullYear();
    }

    const footerLogoContainer = document.getElementById("footer-logo-container");
    if (footerLogoContainer && SITE_CHROME_DEFAULTS.logo) {
        footerLogoContainer.innerHTML = `
            <a href="${SITE_CHROME_DEFAULTS.logo.link}">
                <img src="${SITE_CHROME_DEFAULTS.logo.src}" alt="${SITE_CHROME_DEFAULTS.logo.alt}" class="footer-logo">
            </a>
        `;
    }

    const footerTextContainer = document.getElementById("footer-text-container");
    if (footerTextContainer && SITE_CHROME_DEFAULTS.footerAciklama) {
        footerTextContainer.innerHTML = SITE_CHROME_DEFAULTS.footerAciklama.join("<br>");
    }

    const socialLinksContainer = document.getElementById("social-links-container");
    if (socialLinksContainer && SITE_CHROME_DEFAULTS.sosyalMedya) {
        SITE_CHROME_DEFAULTS.sosyalMedya.forEach(item => {
            const li = document.createElement("li");
            const a = document.createElement("a");
            a.href = item.url;
            a.target = "_blank";
            a.rel = "noopener noreferrer";
            a.innerHTML = `${item.ikon} <span>${item.isim}</span>`;
            // Antigravity ekledi/değiştirdi: GÖREV 4 - Footer'daki sosyal medya bağlantılarına tıklamaları analitik loglara kaydetme
            a.addEventListener("click", () => {
                gonderTelemetriVerisi("social_media", "social_link_click", {
                    platformName: item.isim,
                    targetUrl: item.url
                });
            });
            li.appendChild(a);
            socialLinksContainer.appendChild(li);
        });
    }

    // Header ve Footer DOM'u oluştuktan HEMEN sonra Mega Menü ve Site Ayarlarını MySQL'den doldur
    // (DOMContentLoaded sırası yarışını kesin olarak önler)
    if (window.SkimsPageSections) {
        if (typeof SkimsPageSections.hydrateMegaMenuFromDatabase === "function") {
            SkimsPageSections.hydrateMegaMenuFromDatabase();
        }
        if (typeof SkimsPageSections.applyGlobalSiteSettingsAndVisibility === "function") {
            SkimsPageSections.applyGlobalSiteSettingsAndVisibility();
        }
    }

    // ========================================================
    // 2. AKILLI HEADER SCROLL ANİMASYONU (ORİJİNAL FARK KORUNUR!)
    //    - Ana Sayfa (page-home): Logo + Menü birlikte piksel bazlı hareket eder
    //    - Kategori Sayfaları (page-category): Sadece Logo gizlenir, Menü tepede sabit kalır
    // ========================================================
    const headerTop = document.querySelector(".header-top");
    const headerBottom = document.querySelector(".header-bottom");

    if (headerTop && headerBottom) {
        if (isCategoryPage) {
            // --- KATEGORİ SAYFALARI (ESKİ ek_1.js ORİJİNAL DAVRANIŞI) ---
            let lastScrollY = window.scrollY;
            let isScrollTicking = false;
            // Antigravity ekledi/değiştirdi: Bölüm C ve sayfa genelinde kaydırma sırasında oluşan ekran titremesini (scroll-jitter döngüsünü) önlemek için requestAnimationFrame ve tolerans eşiği (hysteresis) eklendi
            window.addEventListener("scroll", () => {
                if (isScrollTicking) return;
                isScrollTicking = true;

                window.requestAnimationFrame(() => {
                    const currentScrollY = window.scrollY;
                    const scrollDelta = currentScrollY - lastScrollY;

                    // -- A: Pürüzsüz Aşağı/Yukarı Animasyonu (SADECE LOGO İÇİN, Titreme Önleyici Eşikli) --
                    if (scrollDelta > 8 && currentScrollY > 110) {
                        headerTop.classList.add("hide-logo");
                        lastScrollY = currentScrollY;
                    } else if (scrollDelta < -15 || currentScrollY <= 30) {
                        headerTop.classList.remove("hide-logo");
                        lastScrollY = currentScrollY;
                    }

                    // -- B: Dinamik Video Bölümüne gelince menüyü siyah yapma --
                    const videoSections = Array.from(document.querySelectorAll(".carousel-section"))
                        .filter(el => el.offsetParent !== null && el.getBoundingClientRect().height > 0);
                    const inAnyVideoSection = videoSections.some(sec => {
                        const videoRect = sec.getBoundingClientRect();
                        return videoRect.top <= headerBottom.offsetHeight + 10 && videoRect.bottom >= 50;
                    });
                    if (inAnyVideoSection) {
                        headerBottom.classList.add("dark-mode");
                    } else {
                        headerBottom.classList.remove("dark-mode");
                    }

                    isScrollTicking = false;
                });
            }, { passive: true });
        } else {
            // --- ANA SAYFA VE DİĞER SAYFALAR (ESKİ main.js ORİJİNAL DAVRANIŞI) ---
            // Logo ve Menüyü Tek Kapsayıcıda Birleştiriyoruz
            const wrapper = document.createElement("div");
            wrapper.className = "smart-header-wrapper";
            headerTop.parentNode.insertBefore(wrapper, headerTop);
            wrapper.appendChild(headerTop);
            wrapper.appendChild(headerBottom);

            let lastScrollY = window.scrollY || 0;
            let currentTranslate = 0;

            window.addEventListener("scroll", () => {
                const currentScrollY = window.scrollY;
                const isMobileCompactHeader = window.innerWidth <= 768;
                const totalHeight = wrapper.offsetHeight; // Toplam yükseklik (~120px / mobilde 60px)

                if (isMobileCompactHeader) {
                    currentTranslate = 0;
                    wrapper.style.transform = "translateY(0px)";
                    lastScrollY = currentScrollY;
                } else {
                    const deltaY = currentScrollY - lastScrollY;
                    const topHeight = headerTop.offsetHeight; // Logo yüksekliği (55px)

                    // Piksel bazlı olarak, delta (fark) kadar menüyü itiyoruz veya çekiyoruz
                    currentTranslate -= deltaY;

                    // Yukarı çıkarken sınır (maksimum görünürlük) hesaplaması:
                    // Sadece sayfanın en üstündeysek logonun görünmesine izin ver (maxTranslate = 0)
                    // Sayfanın ortalarındaysak logo gizli, sadece menü görünsün (maxTranslate = -topHeight)
                    let maxTranslate = 0;
                    if (currentScrollY > 0) {
                        maxTranslate = -Math.min(topHeight, currentScrollY);
                    }

                    // Hesaplanmış kaydırma miktarını sınırlar içinde tutuyoruz:
                    if (currentTranslate > maxTranslate) currentTranslate = maxTranslate;
                    // En fazla tamamen saklanacak (totalHeight) kadar yukarı çıkabilir
                    if (currentTranslate < -totalHeight) currentTranslate = -totalHeight;

                    // Menüye piksel bazlı hareketi uyguluyoruz (Gecikmesiz, scroll hızında)
                    wrapper.style.transform = `translateY(${currentTranslate}px)`;
                    lastScrollY = currentScrollY;
                }

                // -- B: Dinamik Video Bölümüne gelince siyah yapma --
                const videoSections = Array.from(document.querySelectorAll(".carousel-section"))
                    .filter(el => el.offsetParent !== null && el.getBoundingClientRect().height > 0);
                const inAnyVideoSection = videoSections.some(sec => {
                    const videoRect = sec.getBoundingClientRect();
                    return videoRect.top <= totalHeight + 10 && videoRect.bottom >= 50;
                });
                if (inAnyVideoSection) {
                    wrapper.classList.add("dark-mode");
                } else {
                    wrapper.classList.remove("dark-mode");
                }
            }, { passive: true });
        }
    }
});
