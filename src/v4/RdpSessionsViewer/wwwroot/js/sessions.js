(function () {
    'use strict';
    const status = document.getElementById('table-status');
    if (!window.jQuery || !jQuery.fn.DataTable || !jQuery.fn.dataTable.Responsive) {
        status.textContent = 'Table controls failed to load. Check that the wwwroot/lib files were deployed, then refresh.';
        return;
    }
    const headers = Array.from(document.querySelectorAll('#sessions thead th'), h => h.textContent.trim());
    if (!headers.length) {
        status.textContent = 'No columns available. Check the workbook configuration.';
        return;
    }
    const date = headers.indexOf('LoginDate');
    const time = headers.indexOf('LoginTime');
    const defaults = [];
    if (date >= 0) defaults.push([date, 'desc']);
    if (time >= 0) defaults.push([time, 'desc']);
    const links = Array.from(document.querySelectorAll('[data-report-action]'));
    const bases = links.map(a => a.href);
    const table = jQuery('#sessions').DataTable({
        responsive: true,
        autoWidth: false,
        paging: true,
        pageLength: 10,
        lengthMenu: [10, 25, 50, 100],
        scrollX: false,
        scrollY: '',
        order: defaults,
        orderMulti: false,
        search: { regex: false, smart: false },
        layout: { topStart: 'pageLength', topEnd: 'search', bottomStart: 'info', bottomEnd: 'paging' },
        columnDefs: [
            { targets: '_all', type: 'string' },
            { targets: [headers.indexOf('UserName'), date, time].filter(i => i >= 0), responsivePriority: 1 }
        ],
        language: { search: 'Search records:', emptyTable: 'No session records found.' }
    });
    // Match the server's per-cell substring search and primary column order.
    // LoginDate gets the server's LoginTime tie-break in print/export as well.
    function syncReports() {
        let order = table.order()[0] || [date >= 0 ? date : 0, 'desc'];
        if (order[0] === date) {
            const wanted = [[date, order[1]], ...(time >= 0 ? [[time, order[1]]] : [])];
            if (JSON.stringify(table.order()) !== JSON.stringify(wanted)) {
                table.order(wanted).draw(false);
                return;
            }
        }
        links.forEach((a, i) => {
            const url = new URL(bases[i]);
            url.searchParams.set('search', table.search().trim());
            url.searchParams.set('sort', order[0]);
            url.searchParams.set('direction', order[1]);
            a.href = url.toString();
        });
    }
    // Per-cell search matches the MVC export endpoints, including text containing spaces.
    table.search.fixed('cell-search', function (_text, row) {
        const term = table.search().trim().toLowerCase();
        return !term || row.some(value => String(value).toLowerCase().includes(term));
    });
    table.on('draw', syncReports);
    table.draw();
    syncReports();
    status.hidden = true;
}());
