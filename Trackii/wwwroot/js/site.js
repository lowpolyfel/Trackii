(() => {
    const targets = document.querySelectorAll('.dashboard .card, .dashboard .page-header, .dashboard .chart-grid, .dashboard .kpi-grid');
    if (!targets.length) return;

    targets.forEach((el, index) => {
        el.classList.add('reveal-on-scroll');
        el.style.setProperty('--reveal-delay', `${Math.min(index * 45, 240)}ms`);
    });

    const observer = new IntersectionObserver((entries) => {
        entries.forEach((entry) => {
            if (entry.isIntersecting) {
                entry.target.classList.add('revealed');
                observer.unobserve(entry.target);
            }
        });
    }, { threshold: 0.12, rootMargin: '0px 0px -32px 0px' });

    targets.forEach((el) => observer.observe(el));
})();

(() => {
    const shell = document.querySelector('.app-shell');
    const toggle = document.getElementById('sidebarToggle');
    const overlay = document.getElementById('sidebarOverlay');

    if (!shell || !toggle || !overlay) return;

    const closeSidebar = () => {
        shell.classList.remove('sidebar-open');
        toggle.setAttribute('aria-expanded', 'false');
    };

    const openSidebar = () => {
        shell.classList.add('sidebar-open');
        toggle.setAttribute('aria-expanded', 'true');
    };

    toggle.addEventListener('click', () => {
        if (shell.classList.contains('sidebar-open')) {
            closeSidebar();
            return;
        }

        openSidebar();
    });

    overlay.addEventListener('click', closeSidebar);

    window.addEventListener('resize', () => {
        if (window.innerWidth > 720) {
            closeSidebar();
        }
    });
})();
