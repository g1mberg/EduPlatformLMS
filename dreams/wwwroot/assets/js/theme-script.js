// Immediately apply the theme based on localStorage

(function() {
  const darkMode = localStorage.getItem('darkMode');
  const themeClass = darkMode === 'enabled' ? 'dark-mode' : 'light-mode';

  // Apply the theme class to the document immediately
  document.documentElement.className = themeClass;

  // Wait for DOMContentLoaded to set up event listeners
  document.addEventListener('DOMContentLoaded', () => {
      const darkModeToggle = document.getElementById('dark-mode-toggle');
      const lightModeToggle = document.getElementById('light-mode-toggle');
      const darkModeDropdowns = document.querySelectorAll('.dark-mode-dropdown');
      const lightModeDropdowns = document.querySelectorAll('.light-mode-dropdown');
      const themeDropdownToggles = document.querySelectorAll('.theme-dropdown-toggle');

      const toggleMode = (isDarkMode) => {
          document.documentElement.classList.toggle('dark-mode', isDarkMode);
          localStorage.setItem('darkMode', isDarkMode ? 'enabled' : 'disabled');
          updateToggleButtons(isDarkMode);
      };

      const updateToggleButtons = (isDarkMode) => {
          if (isDarkMode) {
              if (darkModeToggle) darkModeToggle.classList.remove('activate');
              if (lightModeToggle) lightModeToggle.classList.add('activate');
              themeDropdownToggles.forEach(t => t.textContent = 'Dark');
          } else {
              if (lightModeToggle) lightModeToggle.classList.remove('activate');
              if (darkModeToggle) darkModeToggle.classList.add('activate');
              themeDropdownToggles.forEach(t => t.textContent = 'Light');
          }
      };

      // Initial activation based on current theme
      updateToggleButtons(themeClass === 'dark-mode');

      // Add event listeners if elements are present
      if (darkModeToggle && lightModeToggle) {
          darkModeToggle.addEventListener('click', () => toggleMode(true));
          lightModeToggle.addEventListener('click', () => toggleMode(false));
      }
      
      darkModeDropdowns.forEach(dropdown => {
          dropdown.addEventListener('click', () => toggleMode(true));
      });
      lightModeDropdowns.forEach(dropdown => {
          dropdown.addEventListener('click', () => toggleMode(false));
      });
  });
})();