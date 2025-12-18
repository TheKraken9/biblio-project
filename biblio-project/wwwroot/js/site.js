// Site-wide JavaScript functionality

// Fonction utilitaire pour afficher des messages toast (optionnel)
function showToast(message, type = 'info') {
    // Vous pouvez implémenter un système de notifications toast ici
    console.log(`[${type.toUpperCase()}] ${message}`);
}

// Confirmation avant suppression
function confirmDelete(message = 'Êtes-vous sûr de vouloir supprimer cet élément ?') {
    return confirm(message);
}

// Format de date français
function formatDate(date) {
    const options = { year: 'numeric', month: '2-digit', day: '2-digit' };
    return new Date(date).toLocaleDateString('fr-FR', options);
}

// Gestion des erreurs API
function handleApiError(error) {
    console.error('Erreur API:', error);
    alert('Une erreur est survenue. Veuillez réessayer.');
}

// Initialisation au chargement de la page
document.addEventListener('DOMContentLoaded', function() {
    // Activer les tooltips Bootstrap
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    var tooltipList = tooltipTriggerList.map(function (tooltipTriggerEl) {
        return new bootstrap.Tooltip(tooltipTriggerEl);
    });

    // Activer les popovers Bootstrap
    var popoverTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="popover"]'));
    var popoverList = popoverTriggerList.map(function (popoverTriggerEl) {
        return new bootstrap.Popover(popoverTriggerEl);
    });
});