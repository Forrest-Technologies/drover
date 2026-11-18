// The platform's runtime asks the browser a few fixed questions by evaluating small expressions as script.
// Drover's content security policy allows no script evaluation, and that is not widened for this: each of those
// expressions is answered here by a function that does the same thing. Anything not on this list still goes to
// the browser's own eval, which the policy refuses, so a new expression fails loudly instead of slipping through.
(function () {
    const browserEval = globalThis.eval;
    const answers = {
        "window.location.href": () => window.location.href,
        "/Mac|iPhone|iPad|iPod/.test(navigator?.platform ?? '')": () => /Mac|iPhone|iPad|iPod/.test(navigator?.platform ?? ''),
        "navigator?.userAgent ?? ''": () => navigator?.userAgent ?? '',
        "navigator?.platform ?? ''": () => navigator?.platform ?? '',
        "(navigator?.maxTouchPoints ?? 0) > 0": () => (navigator?.maxTouchPoints ?? 0) > 0
    };

    globalThis.eval = function (expression) {
        const answer = answers[String(expression).trim()];
        return answer ? answer() : browserEval(expression);
    };
})();
