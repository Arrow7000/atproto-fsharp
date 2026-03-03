// Automatically scroll to the active aside menu item.
const mainMenu = document.getElementById('fsdocs-main-menu');
function scrollToActiveItem(activeItem) {
    const halfMainMenuHeight = mainMenu.offsetHeight / 2
    if(activeItem.offsetTop > halfMainMenuHeight){
        mainMenu.scrollTop = (activeItem.offsetTop - halfMainMenuHeight) - (activeItem.offsetHeight / 2);
    }
}

const activeItem = document.querySelector("aside .nav-item.active");
if (activeItem && mainMenu) {
    scrollToActiveItem(activeItem);
}

function scrollToAndExpandSelectedMember() {
    if (location.hash) {
        const details = document.querySelector(`tr > td.fsdocs-member-usage:has(a[href='${location.hash}']) ~ td.fsdocs-member-xmldoc > details`);
        details?.setAttribute('open', 'true');
        const header = document.querySelector(`a[href='${location.hash}']`);
        header?.scrollIntoView({ behavior: 'instant'});
    }
}

scrollToAndExpandSelectedMember();
addEventListener('hashchange', scrollToAndExpandSelectedMember);

if(location.pathname.startsWith('/reference/')) {
    const navHeaders = document.querySelectorAll(".nav-header");
    for (const navHeader of navHeaders) {
        if (navHeader.textContent && navHeader.textContent.trim() === 'API Reference') {
            scrollToActiveItem(navHeader);
        }
    }
}

// --- Tooltip XML doc formatter ---
// fsdocs emits XML doc comments as HTML-escaped text inside <em> tags.
// This transforms them into readable HTML.
function formatTooltips() {
    for (const tip of document.querySelectorAll('.fsdocs-tip')) {
        const em = tip.querySelector('em');
        if (!em) continue;

        // Get the raw escaped XML content
        let xml = em.textContent;
        if (!xml) continue;

        let parts = [];

        // Extract <summary>
        const summary = xml.match(/<summary>([\s\S]*?)<\/summary>/);
        if (summary) {
            let text = summary[1].trim();
            text = inlineXml(text);
            parts.push(`<div class="tip-summary">${text}</div>`);
        }

        // Extract <param> tags
        const params = [...xml.matchAll(/<param\s+name="([^"]*)">([\s\S]*?)<\/param>/g)];
        if (params.length > 0) {
            let paramHtml = params.map(m =>
                `<div class="tip-param"><code>${m[1]}</code>: ${inlineXml(m[2].trim())}</div>`
            ).join('');
            parts.push(`<div class="tip-params">${paramHtml}</div>`);
        }

        // Extract <returns>
        const returns = xml.match(/<returns>([\s\S]*?)<\/returns>/);
        if (returns) {
            parts.push(`<div class="tip-returns"><strong>Returns:</strong> ${inlineXml(returns[1].trim())}</div>`);
        }

        // Extract <remarks>
        const remarks = xml.match(/<remarks>([\s\S]*?)<\/remarks>/);
        if (remarks) {
            let text = remarks[1].trim();
            text = inlineXml(text);
            if (text) parts.push(`<div class="tip-remarks">${text}</div>`);
        }

        if (parts.length > 0) {
            em.innerHTML = parts.join('');
        } else {
            // No recognized XML tags -- hide the em if it's just noise
            em.style.display = 'none';
        }
    }
}

// Convert inline XML tags to HTML
function inlineXml(text) {
    return text
        // Strip example/code blocks first (before converting <c>/<see> to <code>)
        .replace(/<example>[\s\S]*?<\/example>/g, '')
        .replace(/<code>[\s\S]*?<\/code>/g, '')
        // Now convert inline XML to HTML
        .replace(/<see\s+cref="([^"]*)"(?:\s*\/>|>[^<]*<\/see>)/g, (_, ref) => {
            const name = ref.split('.').pop().replace(/^T:/, '');
            return `<code>${name}</code>`;
        })
        .replace(/<c>([\s\S]*?)<\/c>/g, '<code>$1</code>')
        .replace(/<paramref\s+name="([^"]*)"(?:\s*\/>|>[^<]*<\/paramref>)/g, '<code>$1</code>')
        .replace(/<para>([\s\S]*?)<\/para>/g, '<p>$1</p>')
        .trim();
}

formatTooltips();
