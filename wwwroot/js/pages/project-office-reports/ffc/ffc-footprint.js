(function (window, document) {
    'use strict';

    const root = document.querySelector('[data-ffc-footprint]');
    if (!root) {
        return;
    }

    const payloadElement = document.getElementById('ffc-footprint-data');
    let countries = [];
    try {
        countries = JSON.parse(payloadElement?.textContent || '[]');
    } catch (error) {
        console.error('FFC footprint data could not be parsed.', error);
    }

    const countryById = new Map(countries.map(country => [String(country.id), country]));
    const countryByIso = new Map(countries.map(country => [String(country.iso3 || '').toUpperCase(), country]));
    const panelElement = document.getElementById('ffcCountryPanel');
    const panel = panelElement && window.bootstrap?.Offcanvas
        ? window.bootstrap.Offcanvas.getOrCreateInstance(panelElement)
        : null;
    let lastCountryTrigger = null;

    const positionLabels = {
        Installed: 'Installed',
        DeliveredAwaitingInstallation: 'Delivered',
        Planned: 'Planned'
    };

    function formatNumber(value) {
        return Number(value || 0).toLocaleString();
    }

    function createElement(tag, className, text) {
        const element = document.createElement(tag);
        if (className) {
            element.className = className;
        }
        if (text !== undefined && text !== null) {
            element.textContent = String(text);
        }
        return element;
    }

    function appendMetric(container, value, label) {
        const metric = createElement('div');
        metric.append(createElement('strong', null, formatNumber(value)));
        metric.append(createElement('span', null, label));
        container.append(metric);
    }

    function updateSelectedCountryInUrl(countryId) {
        const url = new URL(window.location.href);
        if (countryId) {
            url.searchParams.set('selectedCountryId', String(countryId));
        } else {
            url.searchParams.delete('selectedCountryId');
        }
        window.history.replaceState({}, '', url);
    }

    function renderCountryPanel(country) {
        if (!panelElement || !country) {
            return;
        }

        const title = panelElement.querySelector('[data-ffc-panel-title]');
        const iso = panelElement.querySelector('[data-ffc-panel-iso]');
        const summary = panelElement.querySelector('[data-ffc-panel-summary]');
        const years = panelElement.querySelector('[data-ffc-panel-years]');

        title.textContent = country.name || 'Country footprint';
        iso.textContent = `${country.iso3 || ''} · ${formatNumber(country.total)} units · ${formatNumber(country.recordCount)} record${Number(country.recordCount) === 1 ? '' : 's'}`;
        summary.replaceChildren();
        years.replaceChildren();

        appendMetric(summary, country.installed, 'Installed');
        appendMetric(summary, country.delivered, 'Delivered, awaiting installation');
        appendMetric(summary, country.planned, 'Planned');

        (country.years || []).forEach(year => {
            const section = createElement('section', 'ffc-country-panel-year');
            const header = createElement('div', 'ffc-country-panel-year__header');
            const heading = createElement('div');
            heading.append(createElement('h3', null, year.year));
            heading.append(createElement('small', null, `${formatNumber(year.projectCount)} project${Number(year.projectCount) === 1 ? '' : 's'} · ${formatNumber(year.total)} units`));

            const actions = createElement('div', 'ffc-country-panel-year__actions');
            const workspaceLink = createElement('a', 'btn btn-primary btn-sm', 'Open record');
            workspaceLink.href = year.workspaceUrl || '#';
            const detailsLink = createElement('a', 'btn btn-outline-secondary btn-sm', 'Detailed');
            detailsLink.href = year.detailedUrl || '#';
            actions.append(workspaceLink, detailsLink);
            header.append(heading, actions);
            section.append(header);

            if (year.overallPosition) {
                section.append(createElement('div', 'ffc-country-panel-year__position', year.overallPosition));
            }

            const projectList = createElement('div', 'ffc-country-panel-projects');
            if (!Array.isArray(year.projects) || year.projects.length === 0) {
                projectList.append(createElement('div', 'ffc-country-panel-year__position', 'No project entries have been added.'));
            } else {
                year.projects.forEach(project => {
                    const row = createElement('div', 'ffc-country-panel-project');
                    row.append(createElement('strong', null, project.name || project.ffcName || 'FFC project'));
                    row.append(createElement('span', 'ffc-country-panel-project__position', `${formatNumber(project.quantity)} · ${positionLabels[project.position] || project.position || 'Planned'}`));
                    const progress = project.progress || project.stageSummary || 'No current progress recorded.';
                    row.append(createElement('p', null, progress));
                    projectList.append(row);
                });
            }

            section.append(projectList);
            years.append(section);
        });
    }

    function openCountry(countryId, trigger) {
        const country = countryById.get(String(countryId));
        if (!country || !panel) {
            return;
        }

        lastCountryTrigger = trigger || document.activeElement;
        renderCountryPanel(country);
        updateSelectedCountryInUrl(country.id);
        panel.show();
    }

    document.addEventListener('click', event => {
        const trigger = event.target.closest('[data-ffc-country-trigger]');
        if (!trigger) {
            return;
        }
        event.preventDefault();
        openCountry(trigger.dataset.countryId, trigger);
    });

    if (panelElement) {
        panelElement.addEventListener('shown.bs.offcanvas', () => {
            panelElement.querySelector('.btn-close')?.focus();
        });
        panelElement.addEventListener('hidden.bs.offcanvas', () => {
            updateSelectedCountryInUrl(null);
            if (lastCountryTrigger &&
                typeof lastCountryTrigger.focus === 'function' &&
                document.contains(lastCountryTrigger)) {
                lastCountryTrigger.focus();
            }
            lastCountryTrigger = null;
        });
    }

    const selectedCountryId = root.dataset.selectedCountryId;
    if (selectedCountryId && countryById.has(String(selectedCountryId))) {
        window.setTimeout(() => openCountry(selectedCountryId, null), 0);
    }

    const fullscreenButton = root.querySelector('[data-ffc-fullscreen]');
    fullscreenButton?.addEventListener('click', async () => {
        try {
            if (!document.fullscreenElement) {
                await document.documentElement.requestFullscreen?.();
            } else {
                await document.exitFullscreen?.();
            }
        } catch (error) {
            console.warn('Full-screen mode is unavailable.', error);
        }
    });

    if (root.dataset.view !== 'map') {
        return;
    }

    const mapElement = document.getElementById('ffcFootprintMap');
    const loadingElement = root.querySelector('[data-ffc-map-loading]');
    const errorElement = root.querySelector('[data-ffc-map-error]');
    const legendElement = root.querySelector('[data-ffc-map-legend]');

    if (!mapElement || !window.L) {
        if (loadingElement) loadingElement.hidden = true;
        if (errorElement) errorElement.hidden = false;
        return;
    }

    const metric = root.dataset.metric || 'total';
    const metricConfig = {
        total: { property: 'total', label: 'Total units', color: '#4f63d8' },
        installed: { property: 'installed', label: 'Installed units', color: '#2f8f57' },
        delivered: { property: 'delivered', label: 'Delivered, awaiting installation', color: '#3d73e8' },
        planned: { property: 'planned', label: 'Planned units', color: '#d49424' }
    }[metric] || { property: 'total', label: 'Total units', color: '#4f63d8' };

    function getMetricValue(country) {
        return Number(country?.[metricConfig.property] || 0);
    }

    function niceStep(value) {
        if (!Number.isFinite(value) || value <= 1) {
            return 1;
        }
        const magnitude = 10 ** Math.floor(Math.log10(value));
        const normalized = value / magnitude;
        const factor = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
        return factor * magnitude;
    }

    function buildScale(values) {
        const positive = values.filter(value => value > 0);
        const maximum = positive.length ? Math.max(...positive) : 0;
        if (maximum <= 0) {
            return [];
        }

        const step = niceStep(maximum / 4);
        const ranges = [];
        let start = 1;
        while (start <= maximum && ranges.length < 4) {
            const end = Math.min(maximum, start + step - 1);
            ranges.push({ start, end });
            start = end + 1;
        }
        if (ranges.length && ranges[ranges.length - 1].end < maximum) {
            ranges[ranges.length - 1].end = maximum;
        }
        return ranges;
    }

    function hexToRgb(hex) {
        const normalized = String(hex).replace('#', '');
        const value = Number.parseInt(normalized, 16);
        return {
            r: (value >> 16) & 255,
            g: (value >> 8) & 255,
            b: value & 255
        };
    }

    function shade(alpha) {
        const rgb = hexToRgb(metricConfig.color);
        return `rgba(${rgb.r}, ${rgb.g}, ${rgb.b}, ${alpha})`;
    }

    const scale = buildScale(countries.map(getMetricValue));

    function shadeForValue(value) {
        if (value <= 0 || scale.length === 0) {
            return '#e7ecf2';
        }
        const index = scale.findIndex(range => value >= range.start && value <= range.end);
        const resolvedIndex = index < 0 ? scale.length - 1 : index;
        return shade(0.28 + (resolvedIndex * 0.18));
    }

    function renderLegend() {
        if (!legendElement) {
            return;
        }
        legendElement.replaceChildren();
        legendElement.append(createElement('span', 'ffc-footprint-legend__title', metricConfig.label));

        if (scale.length === 0) {
            legendElement.append(createElement('div', 'ffc-footprint-legend__item', 'No units in this view'));
            return;
        }

        scale.forEach((range, index) => {
            const item = createElement('div', 'ffc-footprint-legend__item');
            const sample = createElement('span', 'ffc-footprint-legend__sample');
            sample.style.background = shade(0.28 + (index * 0.18));
            const label = range.start === range.end
                ? `${range.start} unit${range.start === 1 ? '' : 's'}`
                : `${range.start}–${range.end} units`;
            item.append(sample, createElement('span', null, label));
            legendElement.append(item);
        });

        const zero = createElement('div', 'ffc-footprint-legend__item');
        const zeroSample = createElement('span', 'ffc-footprint-legend__sample');
        zeroSample.style.background = '#e7ecf2';
        zero.append(zeroSample, createElement('span', null, 'Record present, 0 units'));
        legendElement.append(zero);
    }

    const map = window.L.map(mapElement, {
        zoomControl: true,
        attributionControl: false,
        minZoom: 1,
        maxZoom: 7,
        worldCopyJump: false
    });
    map.setView([18, 15], 2);

    const focusBounds = {
        world: window.L.latLngBounds([[-58, -180], [82, 180]]),
        africa: window.L.latLngBounds([[-38, -20], [38, 55]]),
        southasia: window.L.latLngBounds([[-2, 60], [38, 102]])
    };

    function getFeatureIso(feature) {
        const properties = feature?.properties || {};
        return String(properties.iso3 || properties.ISO_A3 || properties.ADM0_A3 || '').toUpperCase();
    }

    function featureName(feature) {
        const properties = feature?.properties || {};
        return properties.name || properties.NAME || properties.ADMIN || getFeatureIso(feature);
    }

    function countryStyle(feature) {
        const country = countryByIso.get(getFeatureIso(feature));
        if (!country) {
            return {
                fillColor: '#f7f9fc',
                fillOpacity: 1,
                color: '#95a1b2',
                weight: .65,
                opacity: .8
            };
        }

        return {
            fillColor: shadeForValue(getMetricValue(country)),
            fillOpacity: 1,
            color: '#56667a',
            weight: 1.1,
            opacity: 1
        };
    }

    function bindFeature(feature, layer) {
        const iso = getFeatureIso(feature);
        const country = countryByIso.get(iso);
        if (!country) {
            return;
        }

        const tooltip = document.createElement('div');
        tooltip.append(createElement('strong', null, country.name || featureName(feature)));
        tooltip.append(createElement('span', null, `${formatNumber(country.installed)} installed · ${formatNumber(country.delivered)} delivered awaiting installation · ${formatNumber(country.planned)} planned`));
        layer.bindTooltip(tooltip, { className: 'ffc-footprint-tooltip', sticky: true });

        layer.on({
            mouseover: event => {
                event.target.setStyle({ weight: 2.2, color: '#1f3c67' });
                event.target.bringToFront?.();
            },
            mouseout: event => event.target.setStyle(countryStyle(feature)),
            click: event => openCountry(country.id, event.originalEvent?.target)
        });

        layer.on('add', () => {
            const path = layer.getElement?.();
            if (!path) {
                return;
            }
            path.setAttribute('tabindex', '0');
            path.setAttribute('role', 'button');
            path.setAttribute('aria-label', `${country.name}: ${formatNumber(country.installed)} installed, ${formatNumber(country.delivered)} delivered awaiting installation and ${formatNumber(country.planned)} planned. Open details.`);
            path.addEventListener('keydown', event => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    openCountry(country.id, path);
                }
            });
        });
    }

    function setFocus(name, updateUrl) {
        const normalized = Object.prototype.hasOwnProperty.call(focusBounds, name) ? name : 'world';
        map.fitBounds(focusBounds[normalized], { padding: [18, 18], animate: false });
        root.querySelectorAll('[data-ffc-map-focus]').forEach(button => {
            button.classList.toggle('is-active', button.dataset.ffcMapFocus === normalized);
        });
        const focusInput = root.querySelector('input[name="focus"]');
        if (focusInput) {
            focusInput.value = normalized;
        }
        if (updateUrl) {
            const url = new URL(window.location.href);
            url.searchParams.set('focus', normalized);
            window.history.replaceState({}, '', url);
        }
    }

    root.querySelectorAll('[data-ffc-map-focus]').forEach(button => {
        button.addEventListener('click', () => setFocus(button.dataset.ffcMapFocus, true));
    });

    renderLegend();

    fetch(root.dataset.geoUrl, { credentials: 'same-origin' })
        .then(response => {
            if (!response.ok) {
                throw new Error(`GeoJSON request failed with ${response.status}`);
            }
            return response.json();
        })
        .then(geoJson => {
            window.L.geoJSON(geoJson, {
                style: countryStyle,
                onEachFeature: bindFeature
            }).addTo(map);
            setFocus(root.dataset.focus || 'world', false);
            loadingElement?.remove();
            window.setTimeout(() => map.invalidateSize(), 0);
        })
        .catch(error => {
            console.error('FFC footprint map failed.', error);
            if (loadingElement) loadingElement.hidden = true;
            if (errorElement) errorElement.hidden = false;
        });

    document.addEventListener('fullscreenchange', () => {
        window.setTimeout(() => map.invalidateSize(), 80);
    });
})(window, document);
