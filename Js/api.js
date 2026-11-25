if (window.location.hostname === '127.0.0.1') {
    window.location.hostname = 'localhost';
}

// Canlı (Render) API adresi — Render servisini oluşturduktan sonra yalnızca bu satırı güncelleyin
const PROD_API_BASE_URL = "https://ck-web-api.onrender.com";
const LOCAL_API_BASE_URL = "http://localhost:5000";

// ============================================================================
// SKIMS .NET 10 Web API İstemcisi (Fetch API + JWT Oturum & Favori Yönetimi)
// ============================================================================
(function (global) {
    const isLocalHost =
        window.location.hostname === "localhost" ||
        window.location.hostname === "127.0.0.1" ||
        window.location.protocol === "file:";
    const API_BASE_URL = (
        global.SKIMS_API_BASE_URL ||
        (isLocalHost ? LOCAL_API_BASE_URL : PROD_API_BASE_URL)
    ).replace(/\/+$/, "");
    const TOKEN_KEY = "skims_jwt_token";
    const USER_KEY = "skims_auth_user";

    function isTokenExpired(token) {
        if (!token) return true;
        try {
            const parts = String(token).split(".");
            if (parts.length !== 3) return false;
            const b64 = parts[1].replace(/-/g, "+").replace(/_/g, "/");
            const padded = b64 + "=".repeat((4 - (b64.length % 4)) % 4);
            const payload = JSON.parse(atob(padded));
            if (payload && typeof payload.exp === "number") {
                return (payload.exp * 1000) <= (Date.now() + 5000);
            }
            return false;
        } catch {
            return false;
        }
    }

    function getToken() {
        const token = localStorage.getItem(TOKEN_KEY) || "";
        if (token && isTokenExpired(token)) {
            clearSession();
            return "";
        }
        return token;
    }

    function getUser() {
        try {
            const raw = localStorage.getItem(USER_KEY);
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    }

    function isAuthenticated() {
        return Boolean(getToken() && getUser());
    }

    function isAdmin() {
        const user = getUser();
        return Boolean(getToken() && user && String(user.role || "").toLowerCase() === "admin");
    }

    function setSession(authResponse) {
        if (!authResponse || !authResponse.token || !authResponse.user) return;
        const normalizedUser = {
            id: authResponse.user.id,
            uid: String(authResponse.user.id),
            fullName: authResponse.user.fullName,
            displayName: authResponse.user.fullName,
            email: authResponse.user.email,
            role: authResponse.user.role || "User"
        };
        localStorage.setItem(TOKEN_KEY, authResponse.token);
        localStorage.setItem(USER_KEY, JSON.stringify(normalizedUser));
        sessionStorage.setItem(USER_KEY, JSON.stringify(normalizedUser));
    }

    function clearSession() {
        localStorage.removeItem(TOKEN_KEY);
        localStorage.removeItem(USER_KEY);
        sessionStorage.removeItem(USER_KEY);
    }

    function resolveMediaUrl(url) {
        if (!url || url === "#") return "#";
        if (/^https?:\/\//i.test(url)) return url;
        if (url.startsWith("/uploads/")) return `${API_BASE_URL}${url}`;
        if (url.startsWith("../")) return url.replace(/^\.\.\//, "");
        return url;
    }

    function formatPrice(value) {
        if (typeof value === "string" && value.trim() !== "" && isNaN(Number(value))) {
            return value;
        }
        const num = Number(value);
        if (Number.isNaN(num)) return String(value ?? "");
        return new Intl.NumberFormat("tr-TR", {
            minimumFractionDigits: 0,
            maximumFractionDigits: 2
        }).format(num);
    }

    function generateSlug(text) {
        return String(text || "")
            .trim()
            .toLocaleLowerCase("tr")
            .replace(/ç/g, "c").replace(/ğ/g, "g").replace(/ı/g, "i")
            .replace(/ö/g, "o").replace(/ş/g, "s").replace(/ü/g, "u")
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");
    }

    async function request(endpoint, options = {}) {
        const headers = new Headers(options.headers || {});
        const token = getToken();
        if (token && !headers.has("Authorization")) {
            headers.set("Authorization", `Bearer ${token}`);
        }

        // FormData gönderiminde Content-Type başlığını tarayıcıya bırak (boundary otomatik eklenir)
        if (options.body && !(options.body instanceof FormData) && !headers.has("Content-Type")) {
            headers.set("Content-Type", "application/json");
        }

        let response;
        try {
            response = await fetch(`${API_BASE_URL}${endpoint}`, {
                ...options,
                headers
            });
        } catch (networkErr) {
            const err = new Error(`Backend API sunucusuna (${API_BASE_URL}) ulaşılamıyor. API'nin çalıştığından emin olun.`);
            err.status = 0;
            err.method = options.method || "GET";
            err.endpoint = endpoint;
            err.cause = networkErr;
            throw err;
        }

        if (response.status === 401) {
            // Token süresi dolduysa oturumu temizle
            if (options.clearOnUnauthorized !== false && token) {
                clearSession();
            }
        }

        const contentType = response.headers.get("content-type") || "";
        const isJson = contentType.includes("application/json") || contentType.includes("+json");
        let data = null;
        if (isJson) {
            data = await response.json().catch(() => null);
        } else {
            const rawText = await response.text().catch(() => null);
            if (rawText && (rawText.trim().startsWith("{") || rawText.trim().startsWith("["))) {
                try { data = JSON.parse(rawText); } catch { data = rawText; }
            } else {
                data = rawText;
            }
        }

        if (!response.ok) {
            let errorMessage = "";
            if (data && typeof data === "object") {
                if (data.message) {
                    errorMessage = data.message;
                } else if (data.errors) {
                    const firstErrors = Object.entries(data.errors)
                        .flatMap(([field, msgs]) => (Array.isArray(msgs) ? msgs : [msgs]).map(m => `${field}: ${m}`));
                    if (firstErrors.length > 0) errorMessage = firstErrors.join(" | ");
                } else if (data.detail) {
                    errorMessage = data.detail;
                } else if (data.title) {
                    errorMessage = data.title;
                }
            } else if (typeof data === "string" && data.trim()) {
                errorMessage = data.trim().slice(0, 300);
            }

            if (!errorMessage) {
                if (response.status === 401) errorMessage = "Oturum süresi dolmuş veya yetkisiz erişim (Lütfen tekrar giriş yapın).";
                else if (response.status === 403) errorMessage = "Bu işlem için Admin yetkisi gerekiyor.";
                else if (response.status === 404) errorMessage = "İstenen kayıt veya API adresi bulunamadı.";
                else if (response.status === 413) errorMessage = "Yüklenen dosya boyutu sunucu sınırını aşıyor.";
                else errorMessage = `Sunucu hatası (HTTP ${response.status})`;
            }

            const err = new Error(errorMessage);
            err.status = response.status;
            err.method = options.method || "GET";
            err.endpoint = endpoint;
            err.data = data;
            throw err;
        }

        return data;
    }

    const SkimsApi = {
        baseUrl: API_BASE_URL,
        getToken,
        getUser,
        isAuthenticated,
        isAdmin,
        setSession,
        clearSession,
        resolveMediaUrl,
        formatPrice,
        generateSlug,

        // Kategoriler & 3 Seviyeli Menü Hiyerarşisi (MySQL Single Source of Truth)
        getCategoryTree(includeHidden = false) {
            const qs = includeHidden ? "?includeHidden=true" : "";
            return request(`/api/categories/tree${qs}`, { method: "GET" });
        },
        getCategories(includeHidden = false, level = null) {
            const qs = new URLSearchParams();
            if (includeHidden) qs.set("includeHidden", "true");
            if (level !== null && level !== undefined) qs.set("level", String(level));
            const query = qs.toString() ? `?${qs.toString()}` : "";
            return request(`/api/categories${query}`, { method: "GET" });
        },
        createCategory(payload) {
            return request("/api/categories", {
                method: "POST",
                body: JSON.stringify(payload)
            });
        },
        updateCategory(id, payload) {
            return request(`/api/categories/${encodeURIComponent(id)}`, {
                method: "PUT",
                body: JSON.stringify(payload)
            });
        },
        reorderCategories(orderedIds) {
            return request("/api/categories/reorder", {
                method: "POST",
                body: JSON.stringify({ orderedIds })
            });
        },
        updateCategoryVisibility(id, isPublished) {
            return request(`/api/categories/${encodeURIComponent(id)}/visibility`, {
                method: "PATCH",
                body: JSON.stringify({ isPublished: Boolean(isPublished) })
            });
        },
        updateCategoryVisibilityBySlug(slug, isPublished, displayName = "") {
            const qs = displayName ? `?displayName=${encodeURIComponent(displayName)}` : "";
            return request(`/api/categories/by-slug/${encodeURIComponent(slug)}/visibility${qs}`, {
                method: "PATCH",
                body: JSON.stringify({ isPublished: Boolean(isPublished) })
            });
        },
        deleteCategory(id) {
            return request(`/api/categories/${encodeURIComponent(id)}`, { method: "DELETE" });
        },
        getTrashCategories() {
            return request("/api/categories/trash", { method: "GET" });
        },
        restoreTrashCategory(id) {
            return request(`/api/categories/trash/${encodeURIComponent(id)}/restore`, { method: "POST" });
        },
        permanentDeleteTrashCategory(id) {
            return request(`/api/categories/trash/${encodeURIComponent(id)}/permanent`, { method: "DELETE" });
        },
        emptyTrashCategories() {
            return request("/api/categories/trash/empty", { method: "DELETE" });
        },

        // Ürünler
        getProducts(params = {}) {
            const qs = new URLSearchParams();
            if (params.categoryId) qs.set("categoryId", params.categoryId);
            if (params.categorySlug) qs.set("categorySlug", params.categorySlug);
            if (params.sectionKey) qs.set("sectionKey", params.sectionKey);
            if (params.search) qs.set("search", params.search);
            const query = qs.toString() ? `?${qs.toString()}` : "";
            return request(`/api/products${query}`, { method: "GET" });
        },

        // Dinamik Sayfa Bölümleri (Kolaj Bölüm & Yeni Bölüm Ekle — Kategori İzole + Medya Yükleme)
        getPageSections(pageKey = "", categorySlug = "", categoryName = "") {
            const qs = new URLSearchParams();
            if (pageKey) qs.set("pageKey", pageKey);
            if (categorySlug) qs.set("categorySlug", categorySlug);
            if (categoryName) qs.set("categoryName", categoryName);
            const query = qs.toString() ? `?${qs.toString()}` : "";
            return request(`/api/pagesections${query}`, { method: "GET" });
        },
        upsertPageSection(formData) {
            return request("/api/pagesections", {
                method: "POST",
                body: formData
            });
        },
        updatePageSection(id, formData) {
            return request(`/api/pagesections/${encodeURIComponent(id)}`, {
                method: "PUT",
                body: formData
            });
        },
        deletePageSection(id) {
            return request(`/api/pagesections/${encodeURIComponent(id)}`, { method: "DELETE" });
        },
        uploadSectionMedia(file) {
            const fd = new FormData();
            fd.append("image", file);
            return request("/api/pagesections/upload-media", {
                method: "POST",
                body: fd
            });
        },
        getMediaLibrary() {
            return request("/api/pagesections/media-library", { method: "GET" });
        },

        // Sürüm Geçmişi (PageSnapshots)
        getPageSnapshots(pageKey = "", limit = 20) {
            const qs = new URLSearchParams();
            if (pageKey) qs.set("pageKey", pageKey);
            if (limit) qs.set("limit", String(limit));
            const query = qs.toString() ? `?${qs.toString()}` : "";
            return request(`/api/pagesnapshots${query}`, { method: "GET" });
        },
        getPageSnapshotById(id) {
            return request(`/api/pagesnapshots/${encodeURIComponent(id)}`, { method: "GET" });
        },
        createPageSnapshot(payload) {
            return request("/api/pagesnapshots", {
                method: "POST",
                body: JSON.stringify(payload)
            });
        },

        // Genel Site Ayarları (Logo, Logo Boyutu, Üst Menü, Footer, Ana Sayfa Yayın Durumu)
        getSiteSettings() {
            return request("/api/sitesettings", { method: "GET" });
        },
        updateSiteSettings(formData) {
            return request("/api/sitesettings", {
                method: "PUT",
                body: formData
            });
        },

        // Yetkili Admin Yönetimi (Mevcut Adminler, Normal Kullanıcılar, Yetki Ver/Kaldır, Yeni Admin Oluştur)
        getAdminUsers() {
            return request("/api/admin/users/admins", { method: "GET" });
        },
        getRegularUsers(search = "") {
            const qs = search && String(search).trim() ? `?search=${encodeURIComponent(String(search).trim())}` : "";
            return request(`/api/admin/users/regular${qs}`, { method: "GET" });
        },
        createAdminUser(payload) {
            return request("/api/admin/users", {
                method: "POST",
                body: JSON.stringify(payload)
            });
        },
        grantAdminRole(userId, currentAdminPassword) {
            return request(`/api/admin/users/${encodeURIComponent(userId)}/grant`, {
                method: "POST",
                body: JSON.stringify({ currentAdminPassword })
            });
        },
        revokeAdminRole(userId, currentAdminPassword) {
            return request(`/api/admin/users/${encodeURIComponent(userId)}/revoke`, {
                method: "POST",
                body: JSON.stringify({ currentAdminPassword })
            });
        },

        // Kimlik Doğrulama (JWT)
        async register({ fullName, email, password }) {
            const result = await request("/api/auth/register", {
                method: "POST",
                clearOnUnauthorized: false,
                body: JSON.stringify({ fullName, email, password })
            });
            setSession(result);
            return result;
        },
        async login({ email, password }) {
            const result = await request("/api/auth/login", {
                method: "POST",
                clearOnUnauthorized: false,
                body: JSON.stringify({ email, password })
            });
            setSession(result);
            return result;
        },
        getGoogleClientId() {
            return request("/api/auth/google-client-id", { method: "GET", clearOnUnauthorized: false });
        },
        async googleAuth(idToken) {
            const result = await request("/api/auth/google", {
                method: "POST",
                clearOnUnauthorized: false,
                body: JSON.stringify({ idToken })
            });
            setSession(result);
            return result;
        },
        forgotPassword(email) {
            return request("/api/auth/forgot-password", {
                method: "POST",
                clearOnUnauthorized: false,
                body: JSON.stringify({ email })
            });
        },
        resetPassword({ email, code, newPassword, confirmNewPassword }) {
            return request("/api/auth/reset-password", {
                method: "POST",
                clearOnUnauthorized: false,
                body: JSON.stringify({ email, code, newPassword, confirmNewPassword })
            });
        },
        me() {
            return request("/api/auth/me", { method: "GET" });
        },

        // Favoriler (JWT Korumalı)
        getFavorites() {
            return request("/api/favorites", { method: "GET" });
        },
        validateFavoriteSlots(slotKeys) {
            return request("/api/favorites/validate", {
                method: "POST",
                body: JSON.stringify({ slotKeys: Array.isArray(slotKeys) ? slotKeys : [] })
            });
        },
        removeFavorite(productId) {
            return request(`/api/favorites/${encodeURIComponent(productId)}`, { method: "DELETE" });
        },
        toggleFavorite(productId) {
            return request(`/api/favorites/toggle/${encodeURIComponent(productId)}`, { method: "POST" });
        }
    };

    global.SkimsApi = SkimsApi;

    // ========================================================================
    // SAYFA BÖLÜM KAYDI (KOLAJ BÖLÜM) VE DİNAMİK YENİ BÖLÜM RENDER MOTORU
    // ========================================================================
    const SECTION_TEMPLATES = [
        { type: "video-hero", label: "Bölüm Ana Sayfa Video — Tam Ekran Video Slider" },
        { type: "bolum-a",    label: "Bölüm A — Asimetrik İkili Fotoğraf (2'li Statik)" },
        { type: "bolum-b",    label: "Bölüm B — Büyük + 3 Küçük Kaydırmalı Slider (4'lü Gruplu)" },
        { type: "bolum-c",    label: "Bölüm C — 6 Sütunlu Otomatik Fotoğraf Kolajı (6'lı Statik)" },
        { type: "bolum-d",    label: "Bölüm D — Yatay Ürün Slider (Dinamik)" },
        { type: "video",      label: "Bölüm Video — Kategorili Video Carousel (Instagram/TikTok/YouTube)" }
    ];

    // Yalnızca Ana Sayfa (index.html) varsayılan başlangıç bölümlerine sahiptir (her tipten tek temiz şablon).
    // Alt kategoriler (index.html?kategori=<slug>) için sabit demo bölümleri KESİNLİKLE yüklenmez!
    const BUILTIN_PAGE_SECTIONS = {
        "index.html": [
            { sectionKey: "ana-video",   label: "Ana Video (Carousel)",               templateType: "video-hero", displayOrder: 10, brand: "", title: "NEW ESSENTIALS",         subtitle: "Hareketli detaylar, yeni sezonun favorileri.", price: null, discountPercent: null },
            { sectionKey: "bolum-d",     label: "Bölüm D (Ürün Slider)",              templateType: "bolum-d",    displayOrder: 20, brand: "", title: "MD'S PICK",              subtitle: "",                                             price: null, discountPercent: null },
            { sectionKey: "bolum-b",     label: "Bölüm B (Özel Kaydırmalı Slider)",   templateType: "bolum-b",    displayOrder: 30, brand: "", title: "Öne Çıkan Koleksiyonlar", subtitle: "",                                            price: null, discountPercent: null },
            { sectionKey: "bolum-video", label: "Video Bölümü (Trend Videolar)",      templateType: "video",      displayOrder: 40, brand: "", title: "Şu An Trend Olanlar",    subtitle: "",                                             price: null, discountPercent: null }
        ]
    };

    // URL-dostu kategori slug'ını koruyarak kanonik sayfa anahtarı üretir:
    // "index.html" veya "index.html?kategori=canta"
    function normalizePageKey(pageKey) {
        const raw = String(pageKey || "").trim();
        if (!raw) return "index.html";

        const slugMatch = raw.match(/[?&]kategori=([^&#]+)/i);
        if (slugMatch && slugMatch[1]) {
            let slug = slugMatch[1].trim().toLowerCase();
            try { slug = decodeURIComponent(slug).trim().toLowerCase(); } catch (_) {}
            return `index.html?kategori=${slug}`;
        }

        const cleanPath = raw.split(/[?#]/)[0].trim().toLowerCase();
        const parts = cleanPath.split("/");
        const fileName = parts[parts.length - 1] || "index.html";
        return fileName === "" ? "index.html" : fileName;
    }

    function extractCategorySlug(pageKey) {
        const match = String(pageKey || "").match(/[?&]kategori=([^&#]+)/i);
        if (!match || !match[1]) return "";
        try { return decodeURIComponent(match[1]).trim().toLowerCase(); } catch (_) { return match[1].trim().toLowerCase(); }
    }

    function getBuiltInSectionsForPage(pageKey) {
        const key = normalizePageKey(pageKey);
        const list = BUILTIN_PAGE_SECTIONS[key] || [];
        return list.map(s => ({ ...s, isCustom: false }));
    }

    // Sayfadaki yerleşik bölümler + veritabanı/taslak özel bölümlerini ekleme/displayOrder sırasına göre birleştirir
    function buildOrderedSectionsForPage(pageKey, savedSections = []) {
        const normalizedPage = normalizePageKey(pageKey);
        const baseList = getBuiltInSectionsForPage(normalizedPage);
        const pageRecords = (savedSections || []).filter(s => normalizePageKey(s.pageKey) === normalizedPage);

        const byOrder = (a, b) => {
            const oa = (a.displayOrder !== null && a.displayOrder !== undefined) ? Number(a.displayOrder) : 999999;
            const ob = (b.displayOrder !== null && b.displayOrder !== undefined) ? Number(b.displayOrder) : 999999;
            if (oa !== ob) return oa - ob;
            return (Number(a.id) || 0) - (Number(b.id) || 0);
        };

        // Yerleşik bölümlerin güncellenmiş başlık/açıklama bilgilerini üzerine uygula
        const overridesByKey = new Map();
        pageRecords.filter(s => !s.isCustom).forEach(s => overridesByKey.set(s.sectionKey, s));

        const baseKeys = new Set(baseList.map(b => b.sectionKey));
        const ordered = baseList.map(base => {
            const ov = overridesByKey.get(base.sectionKey);
            if (!ov) return { ...base, isVisible: true };
            return {
                ...base,
                id: ov.id,
                isVisible: ov.isVisible !== undefined ? Boolean(ov.isVisible) : true,
                displayOrder: ov.displayOrder ?? base.displayOrder,
                brand: ov.brand ?? base.brand,
                title: ov.title ?? base.title,
                subtitle: ov.subtitle ?? base.subtitle,
                price: ov.price ?? base.price,
                discountPercent: ov.discountPercent ?? base.discountPercent,
                imageUrl: ov.imageUrl ?? base.imageUrl,
                slotsJson: ov.slotsJson ?? base.slotsJson
            };
        });

        // Veritabanında kayıtlı olup baseList dışında kalan isCustom=false kayıtları da koru
        pageRecords
            .filter(s => !s.isCustom && !baseKeys.has(s.sectionKey))
            .sort(byOrder)
            .forEach(s => ordered.push({ ...s, isVisible: s.isVisible !== undefined ? Boolean(s.isVisible) : true }));

        // Dinamik olarak eklenen / klonlanan bölümleri displayOrder ve hedef konuma göre yerleştir
        const customSections = pageRecords.filter(s => s.isCustom);
        const directSections = customSections.filter(cs => !cs.insertAfterSectionKey).sort(byOrder);
        const anchoredSections = customSections.filter(cs => Boolean(cs.insertAfterSectionKey)).sort(byOrder);

        const toItem = (cs) => {
            const templateInfo = SECTION_TEMPLATES.find(t => t.type === cs.templateType);
            return {
                ...cs,
                isCustom: true,
                label: cs.title || (templateInfo ? templateInfo.label : cs.sectionKey)
            };
        };

        directSections.forEach(cs => ordered.push(toItem(cs)));
        ordered.sort(byOrder);

        anchoredSections.forEach(cs => {
            const item = toItem(cs);
            const anchorIdx = ordered.findIndex(x => x.sectionKey === cs.insertAfterSectionKey);
            if (anchorIdx !== -1) {
                ordered.splice(anchorIdx + 1, 0, item);
            } else {
                ordered.push(item);
            }
        });

        return ordered;
    }

    // Çakışmasız benzersiz bölüm kimliği üretir (ör. bolum_c_u17320391_1)
    function generateUniqueSectionKey(templateType, existingSections = []) {
        const prefixMap = {
            "video-hero": "bolum_anavideo",
            "bolum-a":    "bolum_a",
            "bolum-b":    "bolum_b",
            "bolum-c":    "bolum_c",
            "bolum-d":    "bolum_d",
            "video":      "bolum_video"
        };
        const prefix = prefixMap[String(templateType || "").toLowerCase()] || "bolum_ozel";
        const tsPart = String(Date.now()).slice(-8);
        const existingKeys = new Set((existingSections || []).map(s => String(s.sectionKey || "").toLowerCase()));
        let seq = 1;
        let candidate = `${prefix}_u${tsPart}_${seq}`;
        while (existingKeys.has(candidate)) {
            seq++;
            candidate = `${prefix}_u${tsPart}_${seq}_${Math.random().toString(36).slice(2, 5)}`;
        }
        return candidate;
    }

    function parseSlotsJson(slotsJson, fallbackImageUrl = null) {
        let slots = [];
        if (slotsJson) {
            try {
                const parsed = typeof slotsJson === "string" ? JSON.parse(slotsJson) : slotsJson;
                if (Array.isArray(parsed)) slots = parsed.map(s => ({ ...s }));
            } catch (_) {}
        }
        if (fallbackImageUrl && !slots.some(s => Number(s.slot) === 1 && s.imageUrl)) {
            const idx = slots.findIndex(s => Number(s.slot) === 1);
            if (idx !== -1) {
                slots[idx].imageUrl = fallbackImageUrl;
            } else {
                slots.unshift({ slot: 1, imageUrl: fallbackImageUrl });
            }
        }
        return slots;
    }

    function getSlotData(slots, slotNumber) {
        return (slots || []).find(s => Number(s.slot) === Number(slotNumber)) || null;
    }

    function escapeAttr(val) {
        return String(val ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;");
    }

    function normalizeOptionalUrl(linkUrl) {
        const clean = String(linkUrl || "").trim();
        if (!clean || clean === "#") return "";
        return clean;
    }

    // Koşullu tıklanabilirlik: URL varsa yeni sekmede açılan gerçek link (cursor: pointer),
    // URL boşsa (null / "") kesinlikle buton özelliği göstermez (cursor: default).
    function wrapWithConditionalLink(innerHtml, linkUrl, extraClass = "") {
        const clean = normalizeOptionalUrl(linkUrl);
        if (clean) {
            return `<a href="${escapeAttr(clean)}" target="_blank" rel="noopener noreferrer" class="media-clickable ${extraClass}">${innerHtml}</a>`;
        }
        return `<div class="media-static ${extraClass}" style="width:100%; height:100%;">${innerHtml}</div>`;
    }

    function isVideoUrl(url, mediaType = "") {
        if (String(mediaType || "").toLowerCase() === "video") return true;
        return /\.(mp4|webm|mov)(\?|$)/i.test(String(url || ""));
    }

    function getDefaultVideoCarouselData() {
        return {
            mode: "video-carousel-tabs",
            seeMoreText: "Tümünü Gör",
            seeMoreLink: "",
            tabs: [
                {
                    id: "instagram",
                    name: "Instagram",
                    items: [
                        { id: "ig_1", badge: "• Canlı", title: "Yaz Koleksiyonu", subtitle: "Kamera Arkası Görüntüleri", videoUrl: null, mediaType: "video", linkUrl: null },
                        { id: "ig_2", badge: "• Reel", title: "Yeni Ürünler Geldi", subtitle: "Hemen İncele", videoUrl: null, mediaType: "video", linkUrl: null },
                        { id: "ig_3", badge: "• Stil", title: "Kombin Önerileri", subtitle: "Sezonun Favorileri", videoUrl: null, mediaType: "video", linkUrl: null }
                    ]
                },
                {
                    id: "tiktok",
                    name: "TikTok",
                    items: [
                        { id: "tt_1", badge: "• Trend", title: "Günlük Kombin", subtitle: "Sokak Modası", videoUrl: null, mediaType: "video", linkUrl: null },
                        { id: "tt_2", badge: "• Viral", title: "Bunu Denedin mi?", subtitle: "Yeni Akım", videoUrl: null, mediaType: "video", linkUrl: null }
                    ]
                },
                {
                    id: "youtube",
                    name: "YouTube",
                    items: [
                        { id: "yt_1", badge: "• Vlog", title: "Moda Haftası", subtitle: "Bölüm 1", videoUrl: null, mediaType: "video", linkUrl: null },
                        { id: "yt_2", badge: "• Kutu Açılımı", title: "Yeni Sezon Parçalar", subtitle: "Gelin Birlikte Açalım", videoUrl: null, mediaType: "video", linkUrl: null }
                    ]
                }
            ]
        };
    }

    function parseVideoCarouselData(slotsJson) {
        if (slotsJson) {
            try {
                const parsed = typeof slotsJson === "string" ? JSON.parse(slotsJson) : slotsJson;
                if (parsed && !Array.isArray(parsed) && Array.isArray(parsed.tabs)) {
                    return {
                        mode: "video-carousel-tabs",
                        seeMoreText: parsed.seeMoreText ?? "Tümünü Gör",
                        seeMoreLink: parsed.seeMoreLink ?? "",
                        tabs: parsed.tabs.map((t, ti) => ({
                            id: t.id || `tab_${ti + 1}`,
                            name: t.name || `Sekme ${ti + 1}`,
                            items: Array.isArray(t.items) ? t.items.map((it, ii) => ({
                                id: it.id || `${t.id || 'tab'}_${ii + 1}`,
                                badge: it.badge ?? "• Video",
                                title: it.title ?? "",
                                subtitle: it.subtitle ?? "",
                                videoUrl: it.videoUrl || it.imageUrl || null,
                                mediaType: it.mediaType || "video",
                                linkUrl: it.linkUrl || null,
                                previewDataUrl: it.previewDataUrl || null,
                                imageFile: it.imageFile || null,
                                mediaTransform: it.mediaTransform || null
                            })) : []
                        }))
                    };
                }
                // Eski düz dizi formatından sorunsuz göç: tüm öğeleri Instagram sekmesine yerleştir
                if (Array.isArray(parsed) && parsed.length > 0) {
                    const def = getDefaultVideoCarouselData();
                    def.tabs[0].items = parsed.map((sl, idx) => ({
                        id: `mig_${idx + 1}`,
                        badge: sl.brand || "• Video",
                        title: sl.title || `İçerik ${idx + 1}`,
                        subtitle: sl.description || "",
                        videoUrl: sl.imageUrl || null,
                        mediaType: sl.mediaType || "video",
                        linkUrl: sl.linkUrl || null
                    }));
                    return def;
                }
            } catch (_) {}
        }
        return getDefaultVideoCarouselData();
    }

    function getDefaultHeroVideoSlides(title, subtitle) {
        return [
            { slot: 1, imageUrl: null, mediaType: "video", title: title || "NEW ESSENTIALS", description: subtitle || "Hareketli detaylar, yeni sezonun favorileri.", buttonText: "Shop Now", linkUrl: null },
            { slot: 2, imageUrl: null, mediaType: "video", title: "SIGNATURE COLLECTION", description: "Her ana uyum sağlayan mükemmel tasarımlar.", buttonText: "Shop Now", linkUrl: null },
            { slot: 3, imageUrl: null, mediaType: "video", title: "YENİ SEZON", description: "Sınırları zorlayan yepyeni bir koleksiyon.", buttonText: "Shop Now", linkUrl: null }
        ];
    }

    function parseHeroVideoSlides(slotsJson, fallbackImageUrl, title, subtitle) {
        const parsed = parseSlotsJson(slotsJson, fallbackImageUrl);
        if (parsed.length > 0) {
            return parsed.sort((a, b) => Number(a.slot) - Number(b.slot));
        }
        return getDefaultHeroVideoSlides(title, subtitle);
    }

    function bindDynamicHeartButtons(rootEl) {
        if (!rootEl) return;
        rootEl.querySelectorAll(".heart-btn").forEach(btn => {
            if (btn.dataset.heartBound === "1") return;
            btn.dataset.heartBound = "1";
            btn.addEventListener("click", (e) => {
                e.preventDefault();
                e.stopPropagation();
                const urunVerisi = {
                    productId: btn.getAttribute("data-product-id"),
                    marka: btn.getAttribute("data-brand") || "",
                    baslik: btn.getAttribute("data-title") || "",
                    indirim: btn.getAttribute("data-discount") || "%0",
                    fiyat: btn.getAttribute("data-price") || "",
                    resim: btn.getAttribute("data-image") || "",
                    link: btn.getAttribute("data-link") || "#"
                };
                if (global.SkimsFavoritesBridge && typeof global.SkimsFavoritesBridge.handleHeartClick === "function") {
                    global.SkimsFavoritesBridge.handleHeartClick(btn, urunVerisi);
                }
            });
        });
        if (global.SkimsFavoritesBridge && typeof global.SkimsFavoritesBridge.syncHearts === "function") {
            global.SkimsFavoritesBridge.syncHearts();
        }
    }

    // ========================================================================
    // TAHRİBATSIZ (NON-DESTRUCTIVE) MEDYA KADRAJ / DÖNDÜRME / SİMETRİ / KAYDIRMA MOTORU
    // (Admin Paneli, Büyük Modal, Canlı Taslak Önizleme, index.html ve favori.html ortak motoru)
    // ========================================================================
    const DEFAULT_MEDIA_TRANSFORM = Object.freeze({
        fitMode: "cover",
        rotation: 0,
        flipX: false,
        flipY: false,
        offsetX: 0,
        offsetY: 0,
        zoom: 1
    });

    function normalizeMediaTransform(raw) {
        if (!raw || typeof raw !== "object") {
            return { ...DEFAULT_MEDIA_TRANSFORM };
        }
        const fitMode = String(raw.fitMode || "cover").toLowerCase() === "contain" ? "contain" : "cover";
        let rot = Number(raw.rotation) || 0;
        rot = ((Math.round(rot / 90) * 90) % 360 + 360) % 360;
        const flipX = Boolean(raw.flipX);
        const flipY = Boolean(raw.flipY);
        const clamp = (v, min, max) => Math.max(min, Math.min(max, Number.isFinite(Number(v)) ? Number(v) : 0));
        const offsetX = clamp(raw.offsetX, -1, 1);
        const offsetY = clamp(raw.offsetY, -1, 1);
        const zoom = clamp(raw.zoom ?? 1, 1, 4);
        return { fitMode, rotation: rot, flipX, flipY, offsetX, offsetY, zoom };
    }

    function isMediaTransformModified(raw) {
        if (!raw || typeof raw !== "object") return false;
        const t = normalizeMediaTransform(raw);
        return t.fitMode !== "cover"
            || t.rotation !== 0
            || t.flipX
            || t.flipY
            || Math.abs(t.offsetX) > 0.0005
            || Math.abs(t.offsetY) > 0.0005
            || Math.abs(t.zoom - 1) > 0.0005;
    }

    function computeMediaTransformLayout(containerW, containerH, naturalW, naturalH, rawTransform) {
        const t = normalizeMediaTransform(rawTransform);
        if (!containerW || !containerH || !naturalW || !naturalH) {
            return {
                transform: t,
                elW: containerW || 0,
                elH: containerH || 0,
                visW: containerW || 0,
                visH: containerH || 0,
                maxPanX: 0,
                maxPanY: 0,
                tx: 0,
                ty: 0,
                cssTransform: ""
            };
        }

        const isRotated90 = (t.rotation % 180) !== 0;
        const ew = isRotated90 ? naturalH : naturalW;
        const eh = isRotated90 ? naturalW : naturalH;

        const baseScale = t.fitMode === "contain"
            ? Math.min(containerW / ew, containerH / eh)
            : Math.max(containerW / ew, containerH / eh);
        const scale = baseScale * t.zoom;

        const elW = naturalW * scale;
        const elH = naturalH * scale;
        const visW = ew * scale;
        const visH = eh * scale;

        const maxPanX = Math.max(0, (visW - containerW) / 2);
        const maxPanY = Math.max(0, (visH - containerH) / 2);
        const tx = maxPanX > 0.25 ? t.offsetX * maxPanX : 0;
        const ty = maxPanY > 0.25 ? t.offsetY * maxPanY : 0;

        const sx = t.flipX ? -1 : 1;
        const sy = t.flipY ? -1 : 1;
        const cssTransform = `translate(-50%, -50%) translate(${tx.toFixed(2)}px, ${ty.toFixed(2)}px) rotate(${t.rotation}deg) scale(${sx}, ${sy})`;

        return { transform: t, elW, elH, visW, visH, maxPanX, maxPanY, tx, ty, cssTransform };
    }

    let sharedMediaResizeObserver = null;
    const observedMediaPairs = new WeakMap();

    function getMediaNaturalDims(mediaEl) {
        if (!mediaEl) return { nw: 0, nh: 0 };
        if (mediaEl.tagName === "VIDEO") {
            return { nw: mediaEl.videoWidth || 0, nh: mediaEl.videoHeight || 0 };
        }
        return { nw: mediaEl.naturalWidth || 0, nh: mediaEl.naturalHeight || 0 };
    }

    function applyTransformToMediaElement(mediaEl, containerEl, rawTransform) {
        if (!mediaEl || !containerEl) return null;
        const t = normalizeMediaTransform(rawTransform);
        const modified = isMediaTransformModified(t);

        if (!modified) {
            mediaEl.classList.remove("has-media-transform");
            mediaEl.style.width = "";
            mediaEl.style.height = "";
            mediaEl.style.transform = "";
            return computeMediaTransformLayout(containerEl.clientWidth, containerEl.clientHeight, 0, 0, t);
        }

        const applyNow = () => {
            const rect = containerEl.getBoundingClientRect();
            const cw = rect.width || containerEl.clientWidth;
            const ch = rect.height || containerEl.clientHeight;
            const { nw, nh } = getMediaNaturalDims(mediaEl);
            if (!cw || !ch || !nw || !nh) return null;

            const layout = computeMediaTransformLayout(cw, ch, nw, nh, t);
            mediaEl.classList.add("has-media-transform");
            mediaEl.style.width = `${layout.elW.toFixed(2)}px`;
            mediaEl.style.height = `${layout.elH.toFixed(2)}px`;
            mediaEl.style.transform = layout.cssTransform;
            return layout;
        };

        const res = applyNow();
        if (!res) {
            const evtName = mediaEl.tagName === "VIDEO" ? "loadedmetadata" : "load";
            if (mediaEl.dataset.mtLoadBound !== "1") {
                mediaEl.dataset.mtLoadBound = "1";
                mediaEl.addEventListener(evtName, () => {
                    const saved = observedMediaPairs.get(containerEl);
                    if (saved) applyTransformToMediaElement(saved.mediaEl, containerEl, saved.transform);
                }, { once: true });
            }
        }

        observedMediaPairs.set(containerEl, { mediaEl, transform: t });
        if (typeof ResizeObserver !== "undefined") {
            if (!sharedMediaResizeObserver) {
                sharedMediaResizeObserver = new ResizeObserver(entries => {
                    entries.forEach(entry => {
                        const pair = observedMediaPairs.get(entry.target);
                        if (pair && pair.mediaEl && pair.mediaEl.isConnected) {
                            applyTransformToMediaElement(pair.mediaEl, entry.target, pair.transform);
                        }
                    });
                });
            }
            sharedMediaResizeObserver.observe(containerEl);
        }
        return res;
    }

    function hydrateMediaTransformsInRoot(rootEl) {
        if (!rootEl) return;
        rootEl.querySelectorAll("[data-media-transform]").forEach(mediaEl => {
            const rawStr = mediaEl.getAttribute("data-media-transform");
            if (!rawStr) return;
            try {
                const parsed = JSON.parse(rawStr);
                if (!isMediaTransformModified(parsed)) return;
                const container = mediaEl.closest(".media-transform-frame, .slide, .showcase-left, .showcase-right, .banner-image-wrapper, .product-image-wrapper, .image-box, .img-wrapper, .card");
                if (container) {
                    applyTransformToMediaElement(mediaEl, container, parsed);
                }
            } catch (_) {}
        });
    }

    function buildMediaTransformAttr(rawTransform) {
        if (!isMediaTransformModified(rawTransform)) return "";
        return ` data-media-transform="${escapeAttr(JSON.stringify(normalizeMediaTransform(rawTransform)))}"`;
    }

    const SkimsMediaTransform = {
        DEFAULT: DEFAULT_MEDIA_TRANSFORM,
        normalize: normalizeMediaTransform,
        isModified: isMediaTransformModified,
        computeLayout: computeMediaTransformLayout,
        applyToElement: applyTransformToMediaElement,
        hydrateContainer: hydrateMediaTransformsInRoot,
        buildAttr: buildMediaTransformAttr
    };
    global.SkimsMediaTransform = SkimsMediaTransform;

    function createDynamicSectionElement(section) {
        const slots = parseSlotsJson(section.slotsJson, section.imageUrl)
            .sort((a, b) => Number(a.slot) - Number(b.slot));
        const safeTitle = section.title || "";
        const safeSubtitle = section.subtitle || "";

        // ====================================================================
        // 1) BÖLÜM ANA SAYFA VİDEO (video-hero) — index.html Orijinal İskelet
        //    Dinamik video yükleme, alt noktalar/progress bar, play/pause, sonsuz döngü
        // ====================================================================
        if (section.templateType === "video-hero") {
            const slidesData = parseHeroVideoSlides(section.slotsJson, section.imageUrl, safeTitle, safeSubtitle);
            const container = document.createElement("section");
            container.id = section.sectionKey;
            container.className = "carousel-container";
            container.setAttribute("data-dynamic-section-key", section.sectionKey);
            container.setAttribute("data-template-type", "video-hero");

            const slideTrack = document.createElement("div");
            slideTrack.className = "carousel-slide";
            container.appendChild(slideTrack);

            slidesData.forEach((sl, idx) => {
                const slideDiv = document.createElement("div");
                slideDiv.className = "slide";
                const mediaUrl = sl.imageUrl ? SkimsApi.resolveMediaUrl(sl.imageUrl) : "";
                const isVid = mediaUrl && isVideoUrl(mediaUrl, sl.mediaType);
                const mtAttr = buildMediaTransformAttr(sl.mediaTransform);
                const mediaHtml = mediaUrl
                    ? (isVid
                        ? `<video class="carousel-video" muted playsinline${mtAttr}><source src="${escapeAttr(mediaUrl)}" type="video/mp4"></video>`
                        : `<img src="${escapeAttr(mediaUrl)}" alt="${escapeAttr(sl.title || '')}" style="width:100%; height:100%; object-fit:cover;"${mtAttr}>`)
                    : `<div style="width:100%; height:100%; background:linear-gradient(135deg,#1d1d1f 0%,#35353a 100%);"></div>`;

                const linkClean = normalizeOptionalUrl(sl.linkUrl);
                const btnLabel = sl.buttonText || "Shop Now";
                const btnHtml = linkClean
                    ? `<a href="${escapeAttr(linkClean)}" target="_blank" rel="noopener noreferrer" class="shop-btn">${escapeAttr(btnLabel)}</a>`
                    : (btnLabel ? `<span class="shop-btn media-static" style="text-decoration:none; opacity:0.85;">${escapeAttr(btnLabel)}</span>` : "");

                slideDiv.innerHTML = `
                    ${mediaHtml}
                    <div class="slide-content">
                        ${sl.title ? `<h2>${escapeAttr(sl.title)}</h2>` : ""}
                        ${sl.description ? `<p>${escapeAttr(sl.description)}</p>` : ""}
                        ${btnHtml}
                    </div>
                `;
                slideTrack.appendChild(slideDiv);
            });

            const origSlides = Array.from(slideTrack.querySelectorAll(".slide"));
            const originalCount = origSlides.length;
            if (originalCount > 0) {
                if (originalCount > 1) {
                    const firstClone = origSlides[0].cloneNode(true);
                    firstClone.classList.add("clone");
                    slideTrack.appendChild(firstClone);
                }
                const allSlides = Array.from(slideTrack.querySelectorAll(".slide"));
                let counter = 0;
                let isTransitioning = false;
                let isPlaying = true;
                let fallbackTimer = null;

                const pauseIcon = `<svg viewBox="0 0 24 24"><path d="M6 4h4v16H6zm8 0h4v16h-4z"/></svg>`;
                const playIcon = `<svg viewBox="0 0 24 24"><path d="M8 5v14l11-7z"/></svg>`;

                const playPauseBtn = document.createElement("button");
                playPauseBtn.type = "button";
                playPauseBtn.className = "play-pause-btn";
                playPauseBtn.innerHTML = pauseIcon;
                container.appendChild(playPauseBtn);

                const indContainer = document.createElement("div");
                indContainer.className = "carousel-indicators";
                container.appendChild(indContainer);

                const indicators = origSlides.map((_, i) => {
                    const dot = document.createElement("div");
                    dot.className = "indicator";
                    const prog = document.createElement("div");
                    prog.className = "progress";
                    dot.appendChild(prog);
                    dot.addEventListener("click", () => {
                        if (isTransitioning) return;
                        counter = i;
                        updateSlide();
                    });
                    indContainer.appendChild(dot);
                    return dot;
                });

                function clearFallbackTimer() {
                    if (fallbackTimer) { clearInterval(fallbackTimer); fallbackTimer = null; }
                }

                function moveToNext() {
                    if (originalCount <= 1 || isTransitioning) return;
                    counter++;
                    updateSlide();
                }

                function startFallbackProgress(indEl) {
                    clearFallbackTimer();
                    let elapsed = 0;
                    const duration = 5000;
                    fallbackTimer = setInterval(() => {
                        if (!isPlaying) return;
                        elapsed += 100;
                        const pct = Math.min(100, (elapsed / duration) * 100);
                        const p = indEl.querySelector(".progress");
                        if (p) p.style.width = `${pct}%`;
                        if (elapsed >= duration) {
                            clearFallbackTimer();
                            moveToNext();
                        }
                    }, 100);
                }

                function updateSlide() {
                    clearFallbackTimer();
                    isTransitioning = originalCount > 1;
                    isPlaying = true;
                    playPauseBtn.innerHTML = pauseIcon;
                    slideTrack.style.transform = `translateX(-${100 * counter}%)`;

                    const indIdx = counter % originalCount;
                    indicators.forEach((ind, i) => {
                        ind.classList.toggle("active", i === indIdx);
                        const p = ind.querySelector(".progress");
                        if (p) p.style.width = "0%";
                    });

                    allSlides.forEach(s => {
                        const v = s.querySelector("video");
                        if (v) { v.pause(); v.currentTime = 0; }
                    });

                    const activeSlide = allSlides[counter];
                    const activeVid = activeSlide ? activeSlide.querySelector("video") : null;
                    setTimeout(() => {
                        isTransitioning = false;
                        if (activeVid) {
                            activeVid.play().catch(() => startFallbackProgress(indicators[indIdx]));
                        } else if (originalCount > 1) {
                            startFallbackProgress(indicators[indIdx]);
                        }
                    }, 160);
                }

                playPauseBtn.addEventListener("click", () => {
                    const activeVid = allSlides[counter]?.querySelector("video");
                    if (isPlaying) {
                        if (activeVid) activeVid.pause();
                        playPauseBtn.innerHTML = playIcon;
                        isPlaying = false;
                    } else {
                        if (activeVid) activeVid.play().catch(() => {});
                        playPauseBtn.innerHTML = pauseIcon;
                        isPlaying = true;
                    }
                });

                slideTrack.addEventListener("transitionend", () => {
                    isTransitioning = false;
                    if (originalCount > 1 && counter === originalCount) {
                        slideTrack.style.transition = "none";
                        counter = 0;
                        slideTrack.style.transform = "translateX(0%)";
                        const cloneVid = allSlides[originalCount]?.querySelector("video");
                        if (cloneVid) cloneVid.pause();
                        const firstVid = allSlides[0]?.querySelector("video");
                        if (isPlaying && firstVid) firstVid.play().catch(() => {});
                        setTimeout(() => {
                            slideTrack.style.transition = "transform 0.6s cubic-bezier(0.25, 1, 0.5, 1)";
                        }, 15);
                    }
                });

                allSlides.forEach((slEl, idx) => {
                    const vid = slEl.querySelector("video");
                    if (!vid) return;
                    vid.addEventListener("timeupdate", () => {
                        if (counter === idx && vid.duration > 0) {
                            const pct = (vid.currentTime / vid.duration) * 100;
                            const indIdx = counter % originalCount;
                            const p = indicators[indIdx]?.querySelector(".progress");
                            if (p && !isNaN(pct)) p.style.width = `${pct}%`;
                        }
                    });
                    vid.addEventListener("ended", moveToNext);
                });

                // Dokunmatik (touch swipe) ile Ana Sayfa Video slaytları arasında geçiş
                if (originalCount > 1) {
                    let touchStartX = 0;
                    let touchStartY = 0;
                    container.addEventListener("touchstart", (e) => {
                        if (!e.touches || e.touches.length !== 1) return;
                        touchStartX = e.touches[0].clientX;
                        touchStartY = e.touches[0].clientY;
                    }, { passive: true });
                    container.addEventListener("touchend", (e) => {
                        if (!e.changedTouches || e.changedTouches.length !== 1 || isTransitioning) return;
                        const dx = e.changedTouches[0].clientX - touchStartX;
                        const dy = e.changedTouches[0].clientY - touchStartY;
                        if (Math.abs(dx) > 45 && Math.abs(dx) > Math.abs(dy) * 1.2) {
                            if (dx < 0) {
                                moveToNext();
                            } else {
                                counter = counter > 0 ? counter - 1 : originalCount - 1;
                                updateSlide();
                            }
                        }
                    }, { passive: true });
                }

                updateSlide();
            }
            hydrateMediaTransformsInRoot(container);
            return container;
        }

        // ====================================================================
        // 2) BÖLÜM A — ASİMETRİK İKİLİ FOTOĞRAF (ek_1.css Orijinal İskelet)
        // ====================================================================
        if (section.templateType === "bolum-a") {
            const wrap = document.createElement("div");
            wrap.id = section.sectionKey;
            wrap.setAttribute("data-dynamic-section-key", section.sectionKey);
            wrap.setAttribute("data-template-type", "bolum-a");
            wrap.style.width = "100%";

            const s1 = getSlotData(slots, 1);
            const s2 = getSlotData(slots, 2);

            const renderPhoto = (sd, alt) => {
                if (sd && sd.imageUrl) {
                    const mtAttr = buildMediaTransformAttr(sd.mediaTransform);
                    const img = `<img src="${escapeAttr(SkimsApi.resolveMediaUrl(sd.imageUrl))}" alt="${escapeAttr(sd.title || alt)}"${mtAttr}>`;
                    return wrapWithConditionalLink(img, sd.linkUrl);
                }
                return `<div class="media-static" style="width:100%; aspect-ratio:3/4; background:#ffffff; border:1px solid #eceae6;"></div>`;
            };

            wrap.innerHTML = `
                ${(safeTitle || safeSubtitle) ? `
                    <div class="showcase-header" style="margin-top: 80px;">
                        ${safeTitle ? `<h2 class="showcase-title">${escapeAttr(safeTitle)}</h2>` : ""}
                        ${safeSubtitle ? `<p class="showcase-subtitle">${escapeAttr(safeSubtitle)}</p>` : ""}
                    </div>
                ` : `<div style="height:60px;"></div>`}
                <div class="showcase-gallery">
                    <div class="showcase-left">${renderPhoto(s1, "Görsel 1")}</div>
                    <div class="showcase-right">${renderPhoto(s2, "Görsel 2")}</div>
                </div>
            `;
            hydrateMediaTransformsInRoot(wrap);
            return wrap;
        }

        // ====================================================================
        // 3) BÖLÜM B — 4'LÜ GRUPLU SLIDER (ek_1.css / style.css Orijinal İskelet + Kalp Butonları)
        // ====================================================================
        if (section.templateType === "bolum-b") {
            const mainEl = document.createElement("main");
            mainEl.id = section.sectionKey;
            mainEl.className = "main-content";
            mainEl.setAttribute("data-dynamic-section-key", section.sectionKey);
            mainEl.setAttribute("data-template-type", "bolum-b");

            const maxSlot = slots.reduce((m, s) => Math.max(m, Number(s.slot) || 0), 4);
            const totalGroups = Math.max(1, Math.ceil(maxSlot / 4));
            let activeGroup = 0;

            mainEl.innerHTML = `
                <div class="static-main-header">
                    ${safeTitle ? `<h2 class="trend-title">${escapeAttr(safeTitle)}</h2>` : ""}
                </div>
                <div class="slider-viewport">
                    <div class="trend-sliding-content" data-b-sliding></div>
                </div>
                <div class="pagination">
                    <button type="button" class="prev-btn" data-b-prev>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="15 18 9 12 15 6"></polyline></svg>
                    </button>
                    <span class="page-count" data-b-count>1 / ${totalGroups}</span>
                    <button type="button" class="next-btn" data-b-next>
                        <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"></polyline></svg>
                    </button>
                </div>
            `;

            const slidingArea = mainEl.querySelector("[data-b-sliding]");
            const prevBtn = mainEl.querySelector("[data-b-prev]");
            const nextBtn = mainEl.querySelector("[data-b-next]");
            const countEl = mainEl.querySelector("[data-b-count]");

            const renderSlotImgWithHeart = (slotNum, isBig) => {
                const sd = getSlotData(slots, slotNum);
                const imgUrl = sd && sd.imageUrl ? SkimsApi.resolveMediaUrl(sd.imageUrl) : "";
                const pid = `${section.sectionKey}_slot_${slotNum}`;
                const linkClean = normalizeOptionalUrl(sd && sd.linkUrl);
                const mediaEl = imgUrl
                    ? wrapWithConditionalLink(`<img src="${escapeAttr(imgUrl)}" alt="${escapeAttr(sd?.title || 'Görsel')}"${buildMediaTransformAttr(sd?.mediaTransform)}>`, linkClean)
                    : `<div class="media-static" style="width:100%; height:100%; background:#ffffff;"></div>`;
                const heartHtml = imgUrl
                    ? `<button type="button" class="heart-btn bolum-b-heart-btn" aria-label="Favorilere Ekle"
                        data-product-id="${escapeAttr(pid)}"
                        data-brand="${escapeAttr(sd?.brand || safeTitle || '')}"
                        data-title="${escapeAttr(sd?.title || (isBig ? 'Ana Görsel' : 'Ürün'))}"
                        data-price="${escapeAttr(sd?.price || '')}"
                        data-discount="${escapeAttr(sd?.discount ? '%' + String(sd.discount).replace(/%/g, '') : '%0')}"
                        data-image="${escapeAttr(imgUrl)}"
                        data-link="${escapeAttr(linkClean || '#')}">&#9825;</button>`
                    : "";

                if (isBig) {
                    return `<div class="banner-image-wrapper ${linkClean ? '' : 'media-static'}">${mediaEl}${heartHtml}</div>`;
                }
                return `
                    <div class="product-card ${linkClean ? '' : 'card-no-link'}">
                        <div class="product-image-wrapper ${linkClean ? '' : 'media-static'}">${mediaEl}${heartHtml}</div>
                        <div class="product-info-empty">
                            ${sd?.brand ? `<div style="font-size:12px; font-weight:700; color:#111;">${escapeAttr(sd.brand)}</div>` : ""}
                            ${sd?.title ? `<div style="font-size:11.5px; color:#666;">${escapeAttr(sd.title)}</div>` : ""}
                            ${sd?.price ? `<div style="font-size:12px; font-weight:700; color:#111;">${escapeAttr(sd.price)}</div>` : ""}
                        </div>
                    </div>
                `;
            };

            const renderGroup = () => {
                const base = activeGroup * 4;
                const s1 = getSlotData(slots, base + 1);
                const gTitle = (s1 && s1.groupTitle) || "";
                const gSub = (s1 && s1.groupSubtitle) || "";

                slidingArea.innerHTML = `
                    <div class="banner-content-col">${renderSlotImgWithHeart(base + 1, true)}</div>
                    <div class="grid-content-col">
                        <div class="grid-header">
                            ${gTitle ? `<h3 class="grid-title">${escapeAttr(gTitle)}</h3>` : ""}
                            ${gSub ? `<p class="grid-subtitle">${escapeAttr(gSub)}</p>` : ""}
                        </div>
                        <div class="trend-grid">
                            ${renderSlotImgWithHeart(base + 2, false)}
                            ${renderSlotImgWithHeart(base + 3, false)}
                            ${renderSlotImgWithHeart(base + 4, false)}
                        </div>
                    </div>
                `;
                countEl.textContent = `${activeGroup + 1} / ${totalGroups}`;
                prevBtn.disabled = activeGroup === 0;
                nextBtn.disabled = activeGroup >= totalGroups - 1;
                bindDynamicHeartButtons(slidingArea);
                hydrateMediaTransformsInRoot(slidingArea);
            };

            prevBtn.addEventListener("click", () => { if (activeGroup > 0) { activeGroup--; renderGroup(); } });
            nextBtn.addEventListener("click", () => { if (activeGroup < totalGroups - 1) { activeGroup++; renderGroup(); } });

            // Mobilde büyük kapak görseli üzerinde yatay kaydırma (touch swipe) ile 4'lü gruplar arası geçiş
            if (totalGroups > 1) {
                let bTouchStartX = 0;
                let bTouchStartY = 0;
                slidingArea.addEventListener("touchstart", (e) => {
                    if (!e.touches || e.touches.length !== 1) return;
                    if (e.target && e.target.closest(".trend-grid")) { bTouchStartX = 0; return; }
                    bTouchStartX = e.touches[0].clientX;
                    bTouchStartY = e.touches[0].clientY;
                }, { passive: true });
                slidingArea.addEventListener("touchend", (e) => {
                    if (!bTouchStartX || !e.changedTouches || e.changedTouches.length !== 1) return;
                    const dx = e.changedTouches[0].clientX - bTouchStartX;
                    const dy = e.changedTouches[0].clientY - bTouchStartY;
                    bTouchStartX = 0;
                    if (Math.abs(dx) > 50 && Math.abs(dx) > Math.abs(dy) * 1.2) {
                        if (dx < 0 && activeGroup < totalGroups - 1) { activeGroup++; renderGroup(); }
                        else if (dx > 0 && activeGroup > 0) { activeGroup--; renderGroup(); }
                    }
                }, { passive: true });
            }

            renderGroup();
            return mainEl;
        }

        // ====================================================================
        // 4) BÖLÜM D — YATAY ÜRÜN SLIDER (style.css Orijinal İskelet + Kalp Butonları)
        // ====================================================================
        if (section.templateType === "bolum-d") {
            const secEl = document.createElement("section");
            secEl.id = section.sectionKey;
            secEl.className = "slider-section";
            secEl.setAttribute("data-dynamic-section-key", section.sectionKey);
            secEl.setAttribute("data-template-type", "bolum-d");

            const items = slots.filter(s => s.imageUrl || s.title || s.brand);
            const cardsHtml = items.length > 0
                ? items.map((sd, idx) => {
                    const imgUrl = sd.imageUrl ? SkimsApi.resolveMediaUrl(sd.imageUrl) : "";
                    const linkClean = normalizeOptionalUrl(sd.linkUrl);
                    const pid = `${section.sectionKey}_d_${sd.slot || idx + 1}`;
                    const discStr = sd.discount ? `%${String(sd.discount).replace(/%/g, "")}` : "";
                    const imgTag = imgUrl
                        ? `<img src="${escapeAttr(imgUrl)}" alt="${escapeAttr(sd.title || '')}"${buildMediaTransformAttr(sd.mediaTransform)}>`
                        : `<div style="width:100%; height:100%; background:#ffffff; border:1px solid #eceae6;"></div>`;
                    return `
                        <div class="product-card ${linkClean ? '' : 'card-no-link'}" data-product-id="${escapeAttr(pid)}">
                            <div class="img-wrapper">${wrapWithConditionalLink(imgTag, linkClean)}</div>
                            <div class="brand-row">
                                <div class="brand">${escapeAttr(sd.brand || '')}</div>
                                <button type="button" class="heart-btn" aria-label="Favorilere Ekle"
                                    data-product-id="${escapeAttr(pid)}"
                                    data-brand="${escapeAttr(sd.brand || '')}"
                                    data-title="${escapeAttr(sd.title || '')}"
                                    data-discount="${escapeAttr(discStr || '%0')}"
                                    data-price="${escapeAttr(sd.price || '')}"
                                    data-image="${escapeAttr(imgUrl)}"
                                    data-link="${escapeAttr(linkClean || '#')}">&#9825;</button>
                            </div>
                            ${sd.title ? `<div class="title">${escapeAttr(sd.title)}</div>` : ""}
                            ${sd.description ? `<div style="font-size:11.5px; color:#888; margin-bottom:4px;">${escapeAttr(sd.description)}</div>` : ""}
                            ${(sd.price || discStr) ? `<div class="price-row">${discStr ? `<span class="discount">${escapeAttr(discStr)}</span>` : ""}<span class="price">${escapeAttr(sd.price || '')}</span></div>` : ""}
                        </div>
                    `;
                }).join("")
                : Array.from({ length: 6 }, () => `<div class="product-card card-no-link"><div class="img-wrapper"><div style="width:100%;height:100%;background:#fff;border:1px solid #eceae6;"></div></div></div>`).join("");

            secEl.innerHTML = `
                ${safeTitle ? `<h2 class="slider-title">${escapeAttr(safeTitle)}</h2>` : ""}
                <div class="product-slider-container">
                    <button type="button" class="nav-btn prev-btn">&#10094;</button>
                    <div class="product-track">${cardsHtml}</div>
                    <button type="button" class="nav-btn next-btn">&#10095;</button>
                </div>
            `;

            const track = secEl.querySelector(".product-track");
            const prevBtn = secEl.querySelector(".prev-btn");
            const nextBtn = secEl.querySelector(".next-btn");

            const alignBtns = () => {
                const iw = track.querySelector(".img-wrapper");
                if (iw && iw.offsetHeight > 0) {
                    const top = `${iw.offsetHeight / 2}px`;
                    prevBtn.style.top = top;
                    nextBtn.style.top = top;
                }
            };
            const updateVis = () => {
                prevBtn.style.display = track.scrollLeft <= 2 ? "none" : "flex";
                nextBtn.style.display = (track.scrollLeft + track.clientWidth >= track.scrollWidth - 4) ? "none" : "flex";
            };
            const scrollStep = () => {
                const c = track.querySelector(".product-card");
                const visibleStep = window.innerWidth <= 768 ? 2 : 4;
                const gapPx = parseFloat(window.getComputedStyle(track).gap) || 16;
                return c ? (c.getBoundingClientRect().width + gapPx) * visibleStep : 520;
            };
            prevBtn.addEventListener("click", () => track.scrollBy({ left: -scrollStep(), behavior: "smooth" }));
            nextBtn.addEventListener("click", () => track.scrollBy({ left: scrollStep(), behavior: "smooth" }));
            track.addEventListener("scroll", updateVis);
            window.addEventListener("resize", () => { alignBtns(); updateVis(); });
            setTimeout(() => { alignBtns(); updateVis(); }, 100);
            bindDynamicHeartButtons(secEl);
            hydrateMediaTransformsInRoot(secEl);
            return secEl;
        }

        // ====================================================================
        // 5) BÖLÜM VİDEO — KATEGORİLİ VİDEO CAROUSEL (Orijinal 3+ Sekme & 5sn Kuralı!)
        // ====================================================================
        if (section.templateType === "video") {
            const vData = parseVideoCarouselData(section.slotsJson);
            const secEl = document.createElement("section");
            secEl.id = section.sectionKey;
            secEl.className = "carousel-section";
            secEl.setAttribute("data-dynamic-section-key", section.sectionKey);
            secEl.setAttribute("data-template-type", "video");

            const seeMoreClean = normalizeOptionalUrl(vData.seeMoreLink);
            const seeMoreHtml = vData.seeMoreText
                ? (seeMoreClean
                    ? `<a href="${escapeAttr(seeMoreClean)}" target="_blank" rel="noopener noreferrer" class="see-more">${escapeAttr(vData.seeMoreText)}</a>`
                    : `<span class="see-more media-static">${escapeAttr(vData.seeMoreText)}</span>`)
                : "";

            secEl.innerHTML = `
                <div class="carousel-header-container">
                    <div class="carousel-header">
                        <h2>${escapeAttr(safeTitle || "Şu An Trend Olanlar")}</h2>
                        ${seeMoreHtml}
                    </div>
                    <div class="category-tabs" data-vc-tabs></div>
                </div>
                <div class="carousel-wrapper" data-vc-wrapper>
                    <div class="carousel-track" data-vc-track></div>
                </div>
                <div class="carousel-controls">
                    <button type="button" data-vc-prev disabled><i class="fas fa-chevron-left"></i></button>
                    <span data-vc-page style="font-size:15px; font-weight:600; color:white; letter-spacing:2px;">0 / 0</span>
                    <button type="button" data-vc-next disabled><i class="fas fa-chevron-right"></i></button>
                </div>
            `;

            const tabsContainer = secEl.querySelector("[data-vc-tabs]");
            const track = secEl.querySelector("[data-vc-track]");
            const prevBtn = secEl.querySelector("[data-vc-prev]");
            const nextBtn = secEl.querySelector("[data-vc-next]");
            const pageLabel = secEl.querySelector("[data-vc-page]");

            let playSeqTimer = null;
            let activeIdxInGroup = 0;
            let currentPage = 0;

            function stopAll() {
                clearTimeout(playSeqTimer);
                track.querySelectorAll(".card").forEach(c => {
                    const v = c.querySelector("video");
                    if (v) { v.pause(); v.currentTime = 0; }
                    c.classList.remove("is-playing");
                });
            }

            function getPerView() {
                if (window.innerWidth <= 480) return 1;
                if (window.innerWidth <= 768) return 2;
                if (window.innerWidth <= 1024) return 3;
                return 4;
            }

            let activeUpdateCarouselFn = null;
            let vcResizeTimer = null;
            window.addEventListener("resize", () => {
                clearTimeout(vcResizeTimer);
                vcResizeTimer = setTimeout(() => {
                    if (typeof activeUpdateCarouselFn === "function") activeUpdateCarouselFn();
                }, 120);
            });

            // Dokunmatik (touch swipe) ile video kartları arasında kaydırma
            const wrapperEl = secEl.querySelector("[data-vc-wrapper]");
            if (wrapperEl) {
                let vcTouchStartX = 0;
                let vcTouchStartY = 0;
                wrapperEl.addEventListener("touchstart", (e) => {
                    if (!e.touches || e.touches.length !== 1) return;
                    vcTouchStartX = e.touches[0].clientX;
                    vcTouchStartY = e.touches[0].clientY;
                }, { passive: true });
                wrapperEl.addEventListener("touchend", (e) => {
                    if (!e.changedTouches || e.changedTouches.length !== 1) return;
                    const dx = e.changedTouches[0].clientX - vcTouchStartX;
                    const dy = e.changedTouches[0].clientY - vcTouchStartY;
                    if (Math.abs(dx) > 45 && Math.abs(dx) > Math.abs(dy) * 1.2) {
                        if (dx < 0 && !nextBtn.disabled && typeof nextBtn.onclick === "function") nextBtn.onclick();
                        else if (dx > 0 && !prevBtn.disabled && typeof prevBtn.onclick === "function") prevBtn.onclick();
                    }
                }, { passive: true });
            }

            function renderTab(tabObj) {
                stopAll();
                track.style.transition = "none";
                track.style.transform = "translateX(0px)";
                track.innerHTML = "";
                currentPage = 0;
                void track.offsetWidth;
                track.style.transition = "transform 0.5s ease-in-out";

                const items = Array.isArray(tabObj?.items) ? tabObj.items : [];
                if (items.length === 0) {
                    track.innerHTML = `<p style="color:#888; padding:24px;">Bu sekmede henüz video bulunmuyor.</p>`;
                    pageLabel.textContent = "0 / 0";
                    prevBtn.disabled = true;
                    nextBtn.disabled = true;
                    return;
                }

                items.forEach(item => {
                    const card = document.createElement("div");
                    const linkClean = normalizeOptionalUrl(item.linkUrl);
                    card.className = `card ${linkClean ? "media-clickable" : "card-no-link"}`;
                    const resolvedUrl = item.videoUrl ? SkimsApi.resolveMediaUrl(item.videoUrl) : "";
                    const isVid = resolvedUrl && isVideoUrl(resolvedUrl, item.mediaType);

                    const mtAttr = buildMediaTransformAttr(item.mediaTransform);
                    const mediaEl = resolvedUrl
                        ? (isVid
                            ? `<video src="${escapeAttr(resolvedUrl)}" muted loop playsinline preload="metadata"${mtAttr}></video>`
                            : `<img src="${escapeAttr(resolvedUrl)}" alt="${escapeAttr(item.title || '')}" style="width:100%; height:100%; object-fit:cover; display:block;"${mtAttr}>`)
                        : `<div style="width:100%; height:100%; background:#2b2b2e;"></div>`;

                    card.innerHTML = `
                        ${mediaEl}
                        ${item.badge ? `<div class="badge">${escapeAttr(item.badge)}</div>` : ""}
                        ${isVid ? `<div class="play-icon-overlay"><i class="fas fa-play"></i></div>` : ""}
                        <div class="card-info">
                            ${item.title ? `<h3>${escapeAttr(item.title)}</h3>` : ""}
                            ${item.subtitle ? `<p>${escapeAttr(item.subtitle)}</p>` : ""}
                        </div>
                    `;

                    if (linkClean) {
                        card.addEventListener("click", () => {
                            window.open(linkClean, "_blank", "noopener,noreferrer");
                        });
                    }
                    track.appendChild(card);
                });

                const cards = Array.from(track.querySelectorAll(".card"));
                let itemsPerView = getPerView();
                let totalItems = cards.length;
                let totalPages = Math.max(1, Math.ceil(totalItems / itemsPerView));
                let maxIndex = Math.max(0, totalItems - itemsPerView);

                // Orijinal 5 Saniye Kuralı: Görünen gruptaki her video sırayla 5000ms oynar!
                function startGroupSequence() {
                    stopAll();
                    let startIndex = Math.min(currentPage * itemsPerView, maxIndex);
                    const countInPage = Math.min(itemsPerView, totalItems);
                    if (countInPage <= 0) return;

                    const activeCard = cards[startIndex + activeIdxInGroup];
                    if (activeCard) {
                        const vid = activeCard.querySelector("video");
                        if (vid) {
                            activeCard.classList.add("is-playing");
                            vid.play().catch(() => {});
                        }
                        playSeqTimer = setTimeout(() => {
                            activeIdxInGroup = (activeIdxInGroup + 1) % countInPage;
                            startGroupSequence();
                        }, 5000);
                    }
                }

                function updateCarousel() {
                    itemsPerView = getPerView();
                    totalPages = Math.max(1, Math.ceil(totalItems / itemsPerView));
                    maxIndex = Math.max(0, totalItems - itemsPerView);
                    if (currentPage >= totalPages) currentPage = totalPages - 1;

                    const cardWidth = cards[0]?.offsetWidth || 260;
                    const gap = parseFloat(window.getComputedStyle(track).gap) || 20;
                    const targetIndex = Math.min(currentPage * itemsPerView, maxIndex);
                    track.style.transform = `translateX(-${(cardWidth + gap) * targetIndex}px)`;

                    pageLabel.textContent = `${currentPage + 1} / ${totalPages}`;
                    prevBtn.disabled = currentPage === 0;
                    nextBtn.disabled = currentPage >= totalPages - 1;
                    activeIdxInGroup = 0;
                    startGroupSequence();
                }

                activeUpdateCarouselFn = updateCarousel;
                prevBtn.onclick = () => { if (currentPage > 0) { currentPage--; updateCarousel(); } };
                nextBtn.onclick = () => { if (currentPage < totalPages - 1) { currentPage++; updateCarousel(); } };
                hydrateMediaTransformsInRoot(track);
                setTimeout(updateCarousel, 50);
            }

            if (vData.tabs.length > 0) {
                vData.tabs.forEach((tabObj, idx) => {
                    const tabBtn = document.createElement("div");
                    tabBtn.className = `category-tab ${idx === 0 ? "active" : ""}`;
                    tabBtn.textContent = tabObj.name;
                    tabBtn.addEventListener("click", () => {
                        if (tabBtn.classList.contains("active")) return;
                        tabsContainer.querySelectorAll(".category-tab").forEach(t => t.classList.remove("active"));
                        tabBtn.classList.add("active");
                        renderTab(tabObj);
                    });
                    tabsContainer.appendChild(tabBtn);
                });
                renderTab(vData.tabs[0]);
            }

            return secEl;
        }

        // ====================================================================
        // 6) BÖLÜM C — 6 SÜTUNLU OTOMATİK IZGARA (ek_1.css Orijinal İskelet + Kalp Butonları)
        // ====================================================================
        const secC = document.createElement("section");
        secC.id = section.sectionKey;
        secC.className = "section-c-content";
        secC.setAttribute("data-dynamic-section-key", section.sectionKey);
        secC.setAttribute("data-template-type", "bolum-c");

        const cardsHtml = [1, 2, 3, 4, 5, 6].map(n => {
            const sd = getSlotData(slots, n);
            const imgUrl = sd && sd.imageUrl ? SkimsApi.resolveMediaUrl(sd.imageUrl) : "";
            const linkClean = normalizeOptionalUrl(sd && sd.linkUrl);
            const pid = `${section.sectionKey}_c_${n}`;
            const cBrand = (sd && sd.brand) || "";
            const cTitle = (sd && sd.title) || "";
            const cPrice = (sd && sd.price) || "";
            const cDisc = (sd && sd.discount) ? `%${String(sd.discount).replace(/%/g, "")}` : "";
            const imgHtml = imgUrl
                ? `<img src="${escapeAttr(imgUrl)}" alt="${escapeAttr(cTitle || `Görsel ${n}`)}"${buildMediaTransformAttr(sd?.mediaTransform)}>`
                : `<div style="width:100%; height:100%; background:#ffffff; border:1px solid #eceae6;"></div>`;

            return `
                <div class="card ${linkClean ? '' : 'card-no-link'}" data-product-id="${escapeAttr(pid)}">
                    <div class="image-box">${wrapWithConditionalLink(imgHtml, linkClean)}</div>
                    <div class="text-box">
                        <div class="brand-row">
                            <span class="brand-name">${escapeAttr(cBrand)}</span>
                            <button type="button" class="heart-btn" aria-label="Favorilere Ekle"
                                data-product-id="${escapeAttr(pid)}"
                                data-brand="${escapeAttr(cBrand)}"
                                data-title="${escapeAttr(cTitle)}"
                                data-discount="${escapeAttr(cDisc || '%0')}"
                                data-price="${escapeAttr(cPrice)}"
                                data-image="${escapeAttr(imgUrl)}"
                                data-link="${escapeAttr(linkClean || '#')}">&#9825;</button>
                        </div>
                        ${cTitle ? `<p class="product-title">${escapeAttr(cTitle)}</p>` : ""}
                        ${(cPrice || cDisc) ? `<p class="price">${cDisc ? `<span style="color:#ff5e3a; margin-right:5px;">${escapeAttr(cDisc)}</span>` : ""}<strong>${escapeAttr(cPrice)}</strong></p>` : ""}
                    </div>
                </div>
            `;
        }).join("");

        secC.innerHTML = `
            ${(safeTitle || safeSubtitle) ? `<div style="width:100%; max-width:1400px; padding:0 40px; margin:25px auto 0 auto;">${safeTitle ? `<h2 class="trend-title">${escapeAttr(safeTitle)}</h2>` : ""}${safeSubtitle ? `<p style="font-size:14px; color:#666; margin-top:4px;">${escapeAttr(safeSubtitle)}</p>` : ""}</div>` : ""}
            <div class="grid-container">${cardsHtml}</div>
        `;
        bindDynamicHeartButtons(secC);
        hydrateMediaTransformsInRoot(secC);
        return secC;
    }

    function formatSlugToTitle(slug) {
        return String(slug || "")
            .split("-")
            .filter(Boolean)
            .map(w => w.charAt(0).toLocaleUpperCase("tr") + w.slice(1))
            .join(" ");
    }

    // ========================================================================
    // CANLI TASLAK ÖNİZLEME (MÜŞTERİ GÖZÜYLE GÖR — ?preview=draft)
    // ========================================================================
    function getActiveDraftPreview() {
        try {
            const params = new URLSearchParams(window.location.search || "");
            if (params.get("preview") !== "draft") return null;
            const pKey = params.get("previewKey");
            const storageKey = pKey ? `skims_draft_preview_${pKey}` : "skims_draft_preview_latest";
            const raw = localStorage.getItem(storageKey) || localStorage.getItem("skims_draft_preview_latest");
            if (!raw) return null;
            const parsed = JSON.parse(raw);
            if (!parsed || !Array.isArray(parsed.sections)) return null;
            return parsed;
        } catch {
            return null;
        }
    }

    // ========================================================================
    // CANLI MEGA MENÜ & ÜST YATAY MENÜYÜ MYSQL VERİTABANINDAN DİNAMİK OLUŞTURMA
    // (Single Source of Truth = MySQL 3 Seviyeli Ağaç)
    // ========================================================================
    async function hydrateMegaMenuFromDatabase() {
        const baseFile = String(window.location.pathname || "").split("/").pop().toLowerCase();
        if (baseFile === "admin.html") return;

        const sidebarEl = document.querySelector(".mega-sidebar");
        const contentAreaEl = document.querySelector(".mega-content-area");
        const horizontalNavEl = document.querySelector(".main-horizontal-nav");
        if (!sidebarEl || !contentAreaEl) return;

        try {
            const draftPreview = getActiveDraftPreview();
            let tree = null;
            if (draftPreview && Array.isArray(draftPreview.categoryTree) && draftPreview.categoryTree.length > 0) {
                // Canlı Taslak Önizleme: Müşteri gözüyle yalnızca yayında olan başlık/kategorileri göster
                const filterPublished = (nodes) => (nodes || [])
                    .filter(n => n && n.isPublished !== false && !n.isDeleted)
                    .map(n => ({ ...n, children: filterPublished(n.children) }));
                tree = filterPublished(draftPreview.categoryTree);
            } else {
                // Admin oturumu açıksa gizli başlık/kategorileri de görebilsin; normal ziyaretçiler ASLA göremez
                tree = await SkimsApi.getCategoryTree(SkimsApi.isAdmin());
            }
            if (!Array.isArray(tree) || tree.length === 0) return;

            let tabsHtml = "";
            let panelsHtml = "";
            const flatVisibleLeaves = [];

            tree.forEach((mainNode, idx) => {
                const tabId = `db-tab-${mainNode.id}`;
                const activeCls = idx === 0 ? "active" : "";
                const hiddenMark = !mainNode.isPublished ? " (Gizli)" : "";
                tabsHtml += `<button class="mega-tab ${activeCls}" data-target="${tabId}">${escapeAttr(mainNode.name)}${hiddenMark}</button>`;

                let colsHtml = "";
                (mainNode.children || []).forEach(subNode => {
                    const subHiddenMark = !subNode.isPublished ? " (Gizli)" : "";
                    const linksHtml = (subNode.children || []).map(cat => {
                        const url = `index.html?kategori=${encodeURIComponent(cat.slug)}`;
                        flatVisibleLeaves.push({ ad: cat.name, slug: cat.slug, url });
                        const catHiddenMark = !cat.isPublished ? " (Gizli)" : "";
                        return `<a href="${url}" data-category-name="${escapeAttr(cat.name)}">${escapeAttr(cat.name)}${catHiddenMark}</a>`;
                    }).join("");

                    colsHtml += `
                        <div class="mega-column">
                            <h4>${escapeAttr(subNode.name)}${subHiddenMark}</h4>
                            <div class="mega-links">${linksHtml}</div>
                        </div>
                    `;
                });

                panelsHtml += `<div class="mega-content-panel ${activeCls}" id="${tabId}">${colsHtml}</div>`;
            });

            sidebarEl.innerHTML = tabsHtml;
            contentAreaEl.innerHTML = panelsHtml;

            // Mobil Hamburger Menü Çekmecesi (3 Seviyeli Dinamik Akordeon: Ana Başlık -> Alt Başlık -> Kategori)
            const mobileAccordionRoot = document.getElementById("mobile-menu-accordion-root");
            if (mobileAccordionRoot) {
                mobileAccordionRoot.innerHTML = tree.map((mainNode, idx) => {
                    const hiddenMark = !mainNode.isPublished ? " (Gizli)" : "";
                    const subGroupsHtml = (mainNode.children || []).map(subNode => {
                        const subHiddenMark = !subNode.isPublished ? " (Gizli)" : "";
                        const catLinksHtml = (subNode.children || []).map(cat => {
                            const url = `index.html?kategori=${encodeURIComponent(cat.slug)}`;
                            const catHiddenMark = !cat.isPublished ? " (Gizli)" : "";
                            return `<a href="${url}" class="mobile-acc-link" data-category-name="${escapeAttr(cat.name)}" data-parent-group="${escapeAttr(subNode.name)}">${escapeAttr(cat.name)}${catHiddenMark}</a>`;
                        }).join("");

                        return `
                            <div class="mobile-acc-l2">
                                <button type="button" class="mobile-acc-l2-btn" aria-expanded="false">
                                    <span>${escapeAttr(subNode.name)}${subHiddenMark}</span>
                                    <i class="fa-solid fa-chevron-down"></i>
                                </button>
                                <div class="mobile-acc-l2-body">${catLinksHtml}</div>
                            </div>
                        `;
                    }).join("");

                    return `
                        <div class="mobile-acc-l1 ${idx === 0 ? "is-open" : ""}">
                            <button type="button" class="mobile-acc-l1-btn" aria-expanded="${idx === 0 ? "true" : "false"}">
                                <span>${escapeAttr(mainNode.name)}${hiddenMark}</span>
                                <i class="fa-solid fa-chevron-down"></i>
                            </button>
                            <div class="mobile-acc-l1-body">${subGroupsHtml}</div>
                        </div>
                    `;
                }).join("");

                mobileAccordionRoot.querySelectorAll(".mobile-acc-l1-btn").forEach(btn => {
                    btn.addEventListener("click", () => {
                        const parent = btn.closest(".mobile-acc-l1");
                        if (!parent) return;
                        const nextOpen = !parent.classList.contains("is-open");
                        parent.classList.toggle("is-open", nextOpen);
                        btn.setAttribute("aria-expanded", nextOpen ? "true" : "false");
                    });
                });

                mobileAccordionRoot.querySelectorAll(".mobile-acc-l2-btn").forEach(btn => {
                    btn.addEventListener("click", () => {
                        const parent = btn.closest(".mobile-acc-l2");
                        if (!parent) return;
                        const nextOpen = !parent.classList.contains("is-open");
                        parent.classList.toggle("is-open", nextOpen);
                        btn.setAttribute("aria-expanded", nextOpen ? "true" : "false");
                    });
                });

                mobileAccordionRoot.querySelectorAll(".mobile-acc-link").forEach(link => {
                    link.addEventListener("click", () => {
                        if (typeof global.gonderTelemetriVerisi === "function") {
                            global.gonderTelemetriVerisi("mega_menu", "category_link_click", {
                                parentGroup: link.getAttribute("data-parent-group") || "Mobil Menü",
                                categoryName: (link.getAttribute("data-category-name") || link.textContent || "").trim(),
                                targetUrl: link.getAttribute("href") || "#"
                            });
                        }
                        if (typeof global.SkimsCloseMobileDrawer === "function") {
                            global.SkimsCloseMobileDrawer();
                        }
                    });
                });
            }

            // Hover ile sekme geçişlerini ve tıklama telemetrisini yeniden bağla
            const tabs = sidebarEl.querySelectorAll(".mega-tab");
            const panels = contentAreaEl.querySelectorAll(".mega-content-panel");
            tabs.forEach(tab => {
                tab.addEventListener("mouseenter", () => {
                    tabs.forEach(t => t.classList.remove("active"));
                    panels.forEach(p => p.classList.remove("active"));
                    tab.classList.add("active");
                    const targetPanel = document.getElementById(tab.getAttribute("data-target"));
                    if (targetPanel) targetPanel.classList.add("active");
                });
                tab.addEventListener("click", () => {
                    if (typeof global.gonderTelemetriVerisi === "function") {
                        global.gonderTelemetriVerisi("mega_menu", "category_tab_click", {
                            categoryId: tab.getAttribute("data-target") || "",
                            categoryName: (tab.textContent || "").trim()
                        });
                    }
                });
            });

            // Üst yatay menüyü (en çok bakılan 5 görünür kategori) veritabanındaki gerçek görünür kategorilerden oluştur
            if (horizontalNavEl && flatVisibleLeaves.length > 0) {
                let logs = [];
                try { logs = JSON.parse(localStorage.getItem("skims_local_logs_analytics") || "[]"); } catch (_) {}
                const counts = new Map();
                flatVisibleLeaves.forEach(c => counts.set(c.slug, 0));
                if (Array.isArray(logs)) {
                    logs.forEach(entry => {
                        const url = entry?.metadata?.targetUrl || entry?.pageUrl || "";
                        const sl = extractCategorySlug(url);
                        if (sl && counts.has(sl)) counts.set(sl, counts.get(sl) + 1);
                    });
                }
                const ranked = flatVisibleLeaves.slice().sort((a, b) => (counts.get(b.slug) || 0) - (counts.get(a.slug) || 0));
                horizontalNavEl.innerHTML = ranked.slice(0, 5).map(c =>
                    `<a href="${c.url}" data-category-name="${escapeAttr(c.ad)}">${escapeAttr(c.ad.toLocaleUpperCase("tr"))}</a>`
                ).join("");
            }

            // Dinamik oluşturulan Mega Menü ve Üst Yatay Menü linklerine tıklama telemetrisi bağla
            document.querySelectorAll(".mega-column a, .main-horizontal-nav a").forEach(menuLink => {
                if (menuLink.dataset.telemetryBound === "1") return;
                menuLink.dataset.telemetryBound = "1";
                menuLink.addEventListener("click", () => {
                    if (typeof global.gonderTelemetriVerisi !== "function") return;
                    const parentColumn = menuLink.closest(".mega-column");
                    const groupTitle = parentColumn && parentColumn.querySelector("h4")
                        ? parentColumn.querySelector("h4").textContent.trim()
                        : "Üst Yatay Menü";
                    global.gonderTelemetriVerisi("mega_menu", "category_link_click", {
                        parentGroup: groupTitle,
                        categoryName: (menuLink.getAttribute("data-category-name") || menuLink.textContent || "").trim(),
                        targetUrl: menuLink.getAttribute("href") || "#"
                    });
                });
            });
        } catch (err) {
            console.warn("Dinamik Mega Menü veritabanından yüklenemedi:", err.message);
        }
    }

    // Genel Ayarlar (Logo, Üst Menü Duyurusu, Footer)
    async function applyGlobalSiteSettingsAndVisibility() {
        const baseFile = String(window.location.pathname || "").split("/").pop().toLowerCase();
        if (baseFile === "admin.html") return;

        try {
            const draftPreview = getActiveDraftPreview();
            const settings = (draftPreview && draftPreview.siteSettings)
                ? draftPreview.siteSettings
                : await SkimsApi.getSiteSettings().catch(() => null);
            if (settings) {
                if (settings.logoUrl) {
                    const resolvedLogo = SkimsApi.resolveMediaUrl(settings.logoUrl);
                    document.querySelectorAll("#header-logo-container img, .mobile-header-logo img, #footer-logo-container img").forEach(img => {
                        img.src = resolvedLogo;
                        img.alt = "Site Logosu";
                    });
                }
                const rawZoom = Number(settings.logoZoomPercent);
                const clampedZoom = Number.isFinite(rawZoom) ? Math.max(20, Math.min(200, rawZoom)) : 100;
                document.documentElement.style.setProperty("--logo-scale", String(clampedZoom / 100));
                if (settings.footerText) {
                    const footerTextEl = document.getElementById("footer-text-container");
                    if (footerTextEl) {
                        footerTextEl.innerHTML = String(settings.footerText)
                            .split(/\r?\n/)
                            .filter(Boolean)
                            .join("<br>");
                    }
                }
                const bannerText = String(settings.headerBannerText || "").trim();
                const existingBanner = document.getElementById("skims-global-banner");
                if (bannerText) {
                    if (existingBanner) {
                        existingBanner.textContent = bannerText;
                    } else {
                        const bar = document.createElement("div");
                        bar.id = "skims-global-banner";
                        bar.style.cssText = "background:#111; color:#fff; text-align:center; font-size:12px; padding:6px 14px; letter-spacing:0.4px;";
                        bar.textContent = bannerText;
                        document.body.prepend(bar);
                    }
                } else if (existingBanner) {
                    existingBanner.remove();
                }
            }
        } catch (_) {}
    }

    function showAdminDraftPreviewBanner(pageLabel) {
        if (document.getElementById("admin-draft-preview-ribbon")) return;
        const ribbon = document.createElement("div");
        ribbon.id = "admin-draft-preview-ribbon";
        ribbon.style.cssText = "position:sticky; top:0; z-index:9999; background:#fef3c7; color:#92400e; border-bottom:1px solid #f59e0b; padding:10px 20px; font-size:13px; font-weight:600; display:flex; justify-content:center; align-items:center; gap:12px;";
        ribbon.innerHTML = `<span><i class="fa-solid fa-eye-slash"></i> ÖNİZLEME MODU — "${escapeAttr(pageLabel)}" sayfası şu anda <strong>Gizli (Taslak)</strong> durumunda. Normal ziyaretçiler bu sayfayı göremez.</span><a href="admin.html" style="color:#92400e; text-decoration:underline;">Admin Paneline Dön</a>`;
        document.body.prepend(ribbon);
    }

    function ensureDynamicPageHost() {
        let root = document.getElementById("dynamic-page-root");
        if (!root) {
            root = document.createElement("div");
            root.id = "dynamic-page-root";
            const footer = document.querySelector("footer.site-footer");
            if (footer && footer.parentNode) {
                footer.parentNode.insertBefore(root, footer);
            } else {
                document.body.appendChild(root);
            }
        }
        let host = document.getElementById("dynamic-page-sections-host");
        if (!host) {
            root.innerHTML = `<div id="dynamic-page-sections-host"></div>`;
            host = document.getElementById("dynamic-page-sections-host");
        } else {
            host.innerHTML = "";
        }
        return host;
    }

    async function renderDynamicCategoryTemplatePage(slug) {
        const pageKey = `index.html?kategori=${slug}`;
        let categoryTitle = formatSlugToTitle(slug);
        document.title = `${categoryTitle} - SKIMS`;

        const host = ensureDynamicPageHost();

        let isHiddenForCustomers = false;
        try {
            const allCats = await SkimsApi.getCategories(SkimsApi.isAdmin()).catch(() => []);
            const dbCat = (allCats || []).find(c => String(c.slug || "").toLowerCase() === slug.toLowerCase());
            if (dbCat) {
                categoryTitle = dbCat.name;
                document.title = `${categoryTitle} - SKIMS`;
                if (dbCat.isPublished === false) isHiddenForCustomers = true;
            }
        } catch (_) {}

        try {
            const [sections, products] = await Promise.all([
                SkimsApi.getPageSections(pageKey, slug, categoryTitle),
                SkimsApi.getProducts({ categorySlug: slug }).catch(() => [])
            ]);

            if (isHiddenForCustomers && SkimsApi.isAdmin()) {
                showAdminDraftPreviewBanner(categoryTitle);
            }

            const visibleSections = (sections || []).filter(s => s.isVisible !== false);
            const hasSections = visibleSections.length > 0;
            const hasProducts = Array.isArray(products) && products.length > 0;

            if (!hasSections && !hasProducts) {
                host.innerHTML = `
                    <div style="width:90%; max-width:1400px; margin:40px auto; padding:70px 24px; text-align:center; background:#fff; border:1px dashed #d8d5ce; color:#666; font-size:15px;">
                        <strong>${escapeAttr(categoryTitle)}</strong> sayfasında henüz yayında bölüm bulunmuyor.
                    </div>
                `;
                return;
            }

            const orderedSections = buildOrderedSectionsForPage(pageKey, visibleSections);
            orderedSections.forEach(sec => {
                host.appendChild(createDynamicSectionElement(sec));
            });

            if (hasProducts) {
                const prodSection = document.createElement("section");
                prodSection.className = "section-c-content";
                prodSection.innerHTML = `
                    <div class="grid-container">
                        ${products.map(p => {
                            const imgUrl = SkimsApi.resolveMediaUrl(p.imageUrl);
                            const linkClean = normalizeOptionalUrl(p.productLink);
                            return `
                                <div class="card ${linkClean ? '' : 'card-no-link'}" data-product-id="${p.id}">
                                    <div class="image-box">${wrapWithConditionalLink(`<img src="${escapeAttr(imgUrl)}" alt="${escapeAttr(p.title)}">`, linkClean)}</div>
                                    <div class="text-box">
                                        <div class="brand-row">
                                            <span class="brand-name">${escapeAttr(p.brand || '')}</span>
                                            <button type="button" class="heart-btn" aria-label="Favorilere Ekle" data-product-id="${p.id}" data-brand="${escapeAttr(p.brand || '')}" data-title="${escapeAttr(p.title)}" data-price="${escapeAttr(SkimsApi.formatPrice(p.price))}" data-image="${escapeAttr(imgUrl)}" data-link="${escapeAttr(linkClean || '#')}">&#9825;</button>
                                        </div>
                                        <p class="product-title">${escapeAttr(p.title)}</p>
                                        <p class="price"><strong>${escapeAttr(SkimsApi.formatPrice(p.price))}</strong></p>
                                    </div>
                                </div>
                            `;
                        }).join("")}
                    </div>
                `;
                host.appendChild(prodSection);
                bindDynamicHeartButtons(prodSection);
            }
        } catch (err) {
            if (err && (err.status === 404 || (err.data && err.data.isDeleted))) {
                host.innerHTML = `
                    <div style="width:90%; max-width:680px; margin:70px auto; padding:56px 28px; text-align:center; background:#fff; border:1px solid #e5e2dc; color:#444;">
                        <i class="fa-regular fa-circle-xmark" style="font-size:34px; color:#999; margin-bottom:14px; display:block;"></i>
                        <h2 style="font-size:20px; color:#111; margin-bottom:8px;">Sayfa Bulunamadı</h2>
                        <p style="font-size:14px; color:#666; margin-bottom:20px;">Aradığınız kategori sayfası kaldırılmış veya mevcut değil.</p>
                        <a href="index.html" style="display:inline-block; background:#111; color:#fff; padding:10px 22px; text-decoration:none; font-size:13px; font-weight:600;">Ana Sayfaya Dön</a>
                    </div>
                `;
                return;
            }
            if (err && (err.status === 403 || (err.data && err.data.isHidden))) {
                host.innerHTML = `
                    <div style="width:90%; max-width:680px; margin:70px auto; padding:56px 28px; text-align:center; background:#fff; border:1px solid #e5e2dc; color:#444;">
                        <i class="fa-regular fa-eye-slash" style="font-size:34px; color:#999; margin-bottom:14px; display:block;"></i>
                        <h2 style="font-size:20px; color:#111; margin-bottom:8px;">Bu Sayfa Şu Anda Gizli (Yapım Aşamasında)</h2>
                        <p style="font-size:14px; color:#666; margin-bottom:20px;">Aradığınız sayfa henüz yayına alınmamıştır.</p>
                        <a href="index.html" style="display:inline-block; background:#111; color:#fff; padding:10px 22px; text-decoration:none; font-size:13px; font-weight:600;">Ana Sayfaya Dön</a>
                    </div>
                `;
                return;
            }
            console.warn("Kategori sayfası yüklenemedi:", err.message);
        }
    }

    async function renderDynamicSectionsOnCurrentPage() {
        const currentFullKey = normalizePageKey((window.location.pathname || "index.html") + (window.location.search || ""));
        const baseFile = String(window.location.pathname || "").split("/").pop().toLowerCase();
        if (baseFile === "admin.html") return;

        // Header DOM'u hazırsa (ör. manuel yeniden yükleme) Mega Menü ve Site Ayarlarını güncelle;
        // İlk sayfa yüklemesinde app.js Header/Footer iskeletini kurduktan hemen sonra çağırır.
        if (document.querySelector(".mega-sidebar")) {
            hydrateMegaMenuFromDatabase();
            applyGlobalSiteSettingsAndVisibility();
        }

        if (baseFile === "giris_yap.html" || baseFile === "favori.html") return;

        // CANLI TASLAK ÖNİZLEME (?preview=draft) — Kaydedilmemiş admin değişikliklerini gerçek müşteri görünümüyle çiz
        const draftPreview = getActiveDraftPreview();
        if (draftPreview) {
            const host = ensureDynamicPageHost();
            const titleLabel = draftPreview.pageTitle || (draftPreview.isHome ? "Ana Sayfa" : formatSlugToTitle(draftPreview.slug || ""));
            document.title = `${titleLabel} — Canlı Önizleme`;

            const visibleDraftSections = (draftPreview.sections || []).filter(s => s && s.isVisible !== false);
            if (visibleDraftSections.length === 0) {
                host.innerHTML = `
                    <div style="width:90%; max-width:1400px; margin:40px auto; padding:70px 24px; text-align:center; background:#fff; border:1px dashed #d8d5ce; color:#666; font-size:15px;">
                        <strong>${escapeAttr(titleLabel)}</strong> taslağında görünür kolaj bölümü bulunmuyor.
                    </div>
                `;
            } else {
                visibleDraftSections.forEach(sec => {
                    host.appendChild(createDynamicSectionElement(sec));
                });
            }
            return;
        }

        const slug = extractCategorySlug(currentFullKey);
        if (slug) {
            await renderDynamicCategoryTemplatePage(slug);
            return;
        }

        // Ana Sayfa (index.html) — Tamamen MySQL'deki dinamik bölümlerden çizilir
        const host = ensureDynamicPageHost();
        try {
            const [sections, settings] = await Promise.all([
                SkimsApi.getPageSections("index.html").catch(() => []),
                SkimsApi.getSiteSettings().catch(() => null)
            ]);

            if (settings && settings.homeIsPublished === false) {
                if (SkimsApi.isAdmin()) {
                    showAdminDraftPreviewBanner("Ana Sayfa");
                } else {
                    host.innerHTML = `
                        <div style="width:90%; max-width:680px; margin:70px auto; padding:56px 28px; text-align:center; background:#fff; border:1px solid #e5e2dc; color:#444;">
                            <i class="fa-regular fa-eye-slash" style="font-size:34px; color:#999; margin-bottom:14px; display:block;"></i>
                            <h2 style="font-size:20px; color:#111; margin-bottom:8px;">Ana Sayfa Şu Anda Bakım Modunda</h2>
                            <p style="font-size:14px; color:#666;">Sayfa içeriği güncellenmektedir, lütfen daha sonra tekrar ziyaret edin.</p>
                        </div>
                    `;
                    return;
                }
            }

            const visibleSections = (Array.isArray(sections) ? sections : [])
                .filter(s => s.isVisible !== false)
                .sort((a, b) => {
                    const oa = (a.displayOrder !== null && a.displayOrder !== undefined) ? Number(a.displayOrder) : 999999;
                    const ob = (b.displayOrder !== null && b.displayOrder !== undefined) ? Number(b.displayOrder) : 999999;
                    if (oa !== ob) return oa - ob;
                    return (Number(a.id) || 0) - (Number(b.id) || 0);
                });

            if (visibleSections.length === 0) {
                host.innerHTML = `
                    <div style="width:90%; max-width:1400px; margin:40px auto; padding:70px 24px; text-align:center; background:#fff; border:1px dashed #d8d5ce; color:#666; font-size:15px;">
                        Ana sayfada henüz yayında bölüm bulunmuyor. Admin Panelinden kolaj ekleyip yayınlayabilirsiniz.
                    </div>
                `;
                return;
            }

            visibleSections.forEach(sec => {
                host.appendChild(createDynamicSectionElement(sec));
            });
        } catch (err) {
            console.warn("Dinamik ana sayfa bölümleri yüklenemedi:", err.message);
        }
    }

    global.SkimsPageSections = {
        SECTION_TEMPLATES,
        normalizePageKey,
        extractCategorySlug,
        generateSlug,
        generateUniqueSectionKey,
        parseSlotsJson,
        getDefaultVideoCarouselData,
        parseVideoCarouselData,
        getDefaultHeroVideoSlides,
        parseHeroVideoSlides,
        getSlotData,
        getBuiltInSectionsForPage,
        buildOrderedSectionsForPage,
        hydrateMegaMenuFromDatabase,
        applyGlobalSiteSettingsAndVisibility,
        renderDynamicSectionsOnCurrentPage
    };

    document.addEventListener("DOMContentLoaded", () => {
        renderDynamicSectionsOnCurrentPage();
    });
})(window);
