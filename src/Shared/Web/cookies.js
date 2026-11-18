// Reads one cookie by name. Used for the antiforgery token, which the server leaves readable for this purpose.
export function read(name) {
    const prefix = `${name}=`;
    const found = document.cookie.split("; ").find(part => part.startsWith(prefix));
    return found ? decodeURIComponent(found.slice(prefix.length)) : null;
}
