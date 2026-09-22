// Shared helpers for the TrafficHunt MVC pages.

// Posts JSON to an MVC action with the anti-forgery token attached.
function postJson(url, data) {
    var token = $('input[name="__RequestVerificationToken"]').first().val();
    return $.post({
        url: url,
        data: data,
        headers: { 'RequestVerificationToken': token }
    });
}

// Puts a button into its busy state (spinner visible + disabled) while the work runs.
function setBusy(button, busy) {
    var $button = $(button);
    $button.find('.spinner-border').toggleClass('d-none', !busy);
    $button.prop('disabled', busy);
}

// Shows a message on the page-level status strip.
function setStatus(message, isError) {
    var $strip = $('#statusStrip');
    $strip
        .toggleClass('alert-danger', !!isError)
        .toggleClass('alert-secondary', !isError)
        .text(message);
}
