const navToggle = document.querySelector(".nav-toggle");
const navMenu = document.querySelector(".nav-menu");
const navLinks = document.querySelectorAll(".nav-menu a, .button[href^='#']");
const pickupForm = document.querySelector("#pickup-form");
const successMessage = document.querySelector("#form-success");
const errorMessage = document.querySelector("#form-error");
const currentYear = document.querySelector("#current-year");
const thirdPartyAuthority = document.querySelector("#thirdPartyAuthority");
const authorityRelationshipField = document.querySelector("#authorityRelationshipField");
const authorityRelationship = document.querySelector("#authorityRelationship");

const validators = {
  firstName(value) {
    return value.trim() ? "" : "Please enter your first name.";
  },
  lastName(value) {
    return value.trim() ? "" : "Please enter your last name.";
  },
  email(value) {
    const trimmed = value.trim();
    const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

    if (!trimmed) {
      return "Please enter your email address.";
    }

    return emailPattern.test(trimmed) ? "" : "Please enter a valid email address.";
  },
  zipCode(value) {
    const trimmed = value.trim();
    const zipPattern = /^\d{5}(-\d{4})?$/;

    return !trimmed || zipPattern.test(trimmed) ? "" : "Please enter a valid ZIP code.";
  },
  ownershipTransferConfirmed(value, field) {
    return field.checked ? "" : "Please confirm that you own or have authority to provide these items.";
  }
};

if (currentYear) {
  currentYear.textContent = new Date().getFullYear();
}

function trackEvent(eventName, parameters = {}) {
  if (typeof window.gtag !== "function") {
    return;
  }

  window.gtag("event", eventName, parameters);
}

function closeMobileMenu() {
  if (!navToggle || !navMenu) {
    return;
  }

  navToggle.setAttribute("aria-expanded", "false");
  navToggle.setAttribute("aria-label", "Open navigation menu");
  navMenu.classList.remove("is-open");
  document.body.classList.remove("nav-open");
}

function toggleMobileMenu() {
  const isOpen = navToggle.getAttribute("aria-expanded") === "true";

  navToggle.setAttribute("aria-expanded", String(!isOpen));
  navToggle.setAttribute("aria-label", isOpen ? "Open navigation menu" : "Close navigation menu");
  navMenu.classList.toggle("is-open", !isOpen);
  document.body.classList.toggle("nav-open", !isOpen);
}

function scrollToTarget(targetId) {
  const target = document.querySelector(targetId);

  if (!target) {
    return;
  }

  target.scrollIntoView({ behavior: "smooth", block: "start" });
}

function setFieldError(field, message) {
  const errorElement = document.querySelector(`#${field.id}-error`);

  field.classList.toggle("is-invalid", Boolean(message));
  field.setAttribute("aria-invalid", String(Boolean(message)));

  if (errorElement) {
    errorElement.textContent = message;
    field.setAttribute("aria-describedby", errorElement.id);
  }
}

function validateField(field) {
  const validator = validators[field.name];
  const message = validator ? validator(field.value, field) : "";

  setFieldError(field, message);
  return !message;
}

function validateForm(form) {
  const fieldsToValidate = form.querySelectorAll("[name='firstName'], [name='lastName'], [name='email'], [name='zipCode'], [name='ownershipTransferConfirmed']");
  let isValid = true;

  fieldsToValidate.forEach((field) => {
    if (!validateField(field)) {
      isValid = false;
    }
  });

  return isValid;
}

function syncAuthorityRelationship() {
  if (!thirdPartyAuthority || !authorityRelationshipField || !authorityRelationship) {
    return;
  }

  const shouldShow = thirdPartyAuthority.checked;

  authorityRelationshipField.hidden = !shouldShow;

  if (!shouldShow) {
    authorityRelationship.value = "";
    setFieldError(authorityRelationship, "");
  }
}

function buildPayload(form) {
  const formData = new FormData(form);
  const checkboxValue = (fieldName) => {
    const field = form.elements[fieldName];
    return field && field.checked ? "Yes" : "No";
  };
  const firstName = formData.get("firstName").trim();
  const lastName = formData.get("lastName").trim();
  const email = formData.get("email").trim();
  const cellNumber = formData.get("cellNumber").trim();
  const cityTown = formData.get("cityTown").trim();
  const zipCode = formData.get("zipCode").trim();
  const bookEstimate = formData.get("bookEstimate");
  const largeCollection = checkboxValue("largeCollection");
  const clothingPickup = checkboxValue("clothingPickup");
  const ownershipTransferConfirmed = checkboxValue("ownershipTransferConfirmed");
  const thirdPartyAuthority = checkboxValue("thirdPartyAuthority");
  const authorityRelationship = formData.get("authorityRelationship").trim();
  const comments = formData.get("comments").trim();
  const message = [
    `Name: ${firstName} ${lastName}`,
    `Email: ${email}`,
    `Cell Number: ${cellNumber || "Not provided"}`,
    `City / Town: ${cityTown || "Not provided"}`,
    `ZIP Code: ${zipCode || "Not provided"}`,
    `Estimated Number of Books: ${bookEstimate || "Not provided"}`,
    `Large Collection: ${largeCollection}`,
    `Unwanted Clothing Pickup: ${clothingPickup}`,
    `Ownership and Transfer Confirmation Checked: ${ownershipTransferConfirmed}`,
    `Third-Party Authority Checkbox Checked: ${thirdPartyAuthority}`,
    `Authority / Relationship: ${authorityRelationship || "Not provided"}`,
    "",
    "Comments:",
    comments || "Not provided"
  ].join("\n");

  return {
    _subject: "New CNY Book Rescue pickup request",
    _template: "table",
    _honey: formData.get("_honey").trim(),
    name: `${firstName} ${lastName}`,
    email,
    message,
    "First Name": firstName,
    "Last Name": lastName,
    "Email Address": email,
    "Cell Number": cellNumber,
    "City / Town": cityTown,
    "ZIP Code": zipCode,
    "Estimated Number of Books": bookEstimate,
    "Large Collection": largeCollection,
    "Unwanted Clothing Pickup": clothingPickup,
    "Ownership Transfer Confirmed": ownershipTransferConfirmed,
    "Ownership Confirmation Text": "I confirm that I own, or have authority to dispose of, the books and/or media I am offering to CNY Book Rescue. I understand that any items accepted and picked up by CNY Book Rescue are voluntarily transferred to CNY Book Rescue.",
    "Third-Party Authority": thirdPartyAuthority,
    "Authority / Relationship": authorityRelationship,
    Comments: comments
  };
}

async function submitPickupRequest(payload) {
  const endpoint = pickupForm.getAttribute("action");

  if (!endpoint || endpoint.includes("YOUR_EMAIL@example.com")) {
    throw new Error("FormSubmit recipient email is not configured.");
  }

  const response = await fetch(endpoint, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Accept: "application/json"
    },
    body: JSON.stringify(payload)
  });

  let data = {};

  try {
    data = await response.json();
  } catch {
    data = {};
  }

  if (!response.ok || data.success === false) {
    throw new Error(data.message || "FormSubmit request failed.");
  }

  return data;
}

if (navToggle && navMenu) {
  navToggle.addEventListener("click", toggleMobileMenu);
}

if (thirdPartyAuthority) {
  thirdPartyAuthority.addEventListener("change", syncAuthorityRelationship);
  syncAuthorityRelationship();
}

navLinks.forEach((link) => {
  link.addEventListener("click", (event) => {
    const href = link.getAttribute("href");
    const analyticsEvent = link.dataset.analyticsEvent;

    if (!href || !href.startsWith("#")) {
      return;
    }

    event.preventDefault();

    if (analyticsEvent) {
      trackEvent(analyticsEvent, {
        link_text: link.textContent.trim(),
        link_url: href
      });
    }

    closeMobileMenu();
    scrollToTarget(href);
  });
});

document.querySelectorAll("a[href^='tel:'], a[href^='mailto:'], a[href^='sms:']").forEach((link) => {
  link.addEventListener("click", () => {
    const href = link.getAttribute("href") || "";
    const eventName = href.startsWith("tel:")
      ? "phone_click"
      : href.startsWith("mailto:")
        ? "email_click"
        : "sms_click";

    trackEvent(eventName, {
      link_text: link.textContent.trim(),
      link_url: href
    });
  });
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    closeMobileMenu();
  }
});

if (pickupForm) {
  pickupForm.querySelectorAll("input, select, textarea").forEach((field) => {
    field.addEventListener("input", () => {
      if (validators[field.name]) {
        validateField(field);
      }
    });

    field.addEventListener("blur", () => {
      if (validators[field.name]) {
        validateField(field);
      }
    });
  });

  pickupForm.addEventListener("submit", async (event) => {
    event.preventDefault();
    successMessage.hidden = true;
    errorMessage.hidden = true;

    if (!validateForm(pickupForm)) {
      const firstInvalidField = pickupForm.querySelector(".is-invalid");

      if (firstInvalidField) {
        firstInvalidField.focus();
      }

      return;
    }

    const payload = buildPayload(pickupForm);
    const submitButton = pickupForm.querySelector("button[type='submit']");
    const originalButtonText = submitButton.textContent;

    submitButton.disabled = true;
    submitButton.textContent = "Sending...";

    try {
      await submitPickupRequest(payload);

      trackEvent("pickup_request_submit", {
        book_estimate: payload["Estimated Number of Books"] || "Not provided",
        city_town_provided: payload["City / Town"] ? "Yes" : "No",
        zip_code_provided: payload["ZIP Code"] ? "Yes" : "No",
        large_collection: payload["Large Collection"],
        unwanted_clothing_pickup: payload["Unwanted Clothing Pickup"]
      });

      pickupForm.reset();
      syncAuthorityRelationship();
      pickupForm.querySelectorAll(".is-invalid").forEach((field) => setFieldError(field, ""));
      successMessage.hidden = false;
      successMessage.scrollIntoView({ behavior: "smooth", block: "center" });
    } catch (error) {
      console.warn(error);
      errorMessage.textContent = error.message || "Something went wrong while sending your request. Please try again in a moment.";
      errorMessage.hidden = false;
      errorMessage.scrollIntoView({ behavior: "smooth", block: "center" });
    } finally {
      submitButton.disabled = false;
      submitButton.textContent = originalButtonText;
    }
  });
}
