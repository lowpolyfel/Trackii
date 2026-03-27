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
