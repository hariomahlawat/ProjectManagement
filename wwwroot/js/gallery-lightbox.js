function initGallery(gallery) {
  const modalId = gallery.getAttribute('data-gallery-modal-id');
  const Modal = window.bootstrap?.Modal;
  if (!modalId || !Modal) {
    return;
  }

  const modalEl = document.getElementById(modalId);
  if (!modalEl) {
    return;
  }

  const dataEl = modalEl.querySelector('[data-gallery-data]');
  if (!dataEl) {
    return;
  }

  let photos = [];
  try {
    photos = JSON.parse(dataEl.textContent ?? '[]');
  } catch (error) {
    console.error('Failed to parse gallery data', error);
    return;
  }

  if (!Array.isArray(photos) || photos.length === 0) {
    return;
  }

  const modalInstance = typeof Modal.getOrCreateInstance === 'function'
    ? Modal.getOrCreateInstance(modalEl)
    : new Modal(modalEl);

  const imgEl = modalEl.querySelector('[data-gallery-image]');
  const captionEl = modalEl.querySelector('[data-gallery-caption]');
  const counterEl = modalEl.querySelector('[data-gallery-counter]');
  const thumbContainer = modalEl.querySelector('[data-gallery-thumbnails]');
  const prevBtn = modalEl.querySelector('[data-gallery-prev]');
  const nextBtn = modalEl.querySelector('[data-gallery-next]');
  const fallbackEl = modalEl.querySelector('.project-gallery-modal__fallback');

  let currentIndex = 0;

  const updateThumbState = () => {
    if (!thumbContainer) {
      return;
    }

    const buttons = thumbContainer.querySelectorAll('[data-gallery-thumb]');
    buttons.forEach((button, idx) => {
      if (idx === currentIndex) {
        button.classList.add('is-active');
        button.setAttribute('aria-current', 'true');
      } else {
        button.classList.remove('is-active');
        button.removeAttribute('aria-current');
      }
    });
  };

  const updateNavState = () => {
    if (prevBtn) {
      prevBtn.disabled = currentIndex <= 0;
    }
    if (nextBtn) {
      nextBtn.disabled = currentIndex >= photos.length - 1;
    }
  };

  const updateStage = (index) => {
    const nextIndex = Math.min(Math.max(index, 0), photos.length - 1);
    const photo = photos[nextIndex];
    if (!photo || !imgEl) {
      return;
    }

    currentIndex = nextIndex;
    const { stage, caption, alt } = photo;
    if (stage) {
      if (stage.src) {
        imgEl.src = stage.src;
      }
      if (stage.srcSet) {
        imgEl.srcset = stage.srcSet;
      } else {
        imgEl.removeAttribute('srcset');
      }
      if (stage.sizes) {
        imgEl.sizes = stage.sizes;
      } else {
        imgEl.removeAttribute('sizes');
      }
    }

    imgEl.alt = alt ?? '';

    if (captionEl) {
      captionEl.textContent = caption ?? '';
    }

    if (counterEl) {
      counterEl.textContent = `${currentIndex + 1} of ${photos.length}`;
    }

    updateThumbState();
    updateNavState();
  };

  const handlePrev = () => {
    if (currentIndex > 0) {
      updateStage(currentIndex - 1);
    }
  };

  const handleNext = () => {
    if (currentIndex < photos.length - 1) {
      updateStage(currentIndex + 1);
    }
  };

  const handleKey = (event) => {
    if (event.key === 'ArrowLeft') {
      event.preventDefault();
      handlePrev();
    } else if (event.key === 'ArrowRight') {
      event.preventDefault();
      handleNext();
    }
  };

  if (prevBtn) {
    prevBtn.addEventListener('click', (event) => {
      event.preventDefault();
      handlePrev();
    });
  }

  if (nextBtn) {
    nextBtn.addEventListener('click', (event) => {
      event.preventDefault();
      handleNext();
    });
  }

  if (thumbContainer) {
    thumbContainer.innerHTML = '';
    photos.forEach((photo, idx) => {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = 'project-gallery-modal__thumb';
      button.setAttribute('data-gallery-thumb', '');
      button.setAttribute('role', 'listitem');
      button.setAttribute('aria-label', photo.caption ? `View photo: ${photo.caption}` : 'View photo');

      const thumbImage = document.createElement('img');
      thumbImage.className = 'project-gallery-modal__thumb-img';
      if (photo.thumb?.src) {
        thumbImage.src = photo.thumb.src;
      }
      if (photo.thumb?.srcSet) {
        thumbImage.srcset = photo.thumb.srcSet;
      }
      thumbImage.alt = photo.caption ? `Thumbnail: ${photo.caption}` : 'Photo thumbnail';

      button.appendChild(thumbImage);
      button.addEventListener('click', (event) => {
        event.preventDefault();
        updateStage(idx);
      });

      thumbContainer.appendChild(button);
    });
  }

  if (fallbackEl) {
    fallbackEl.hidden = true;
  }

  const openModalAt = (photoId) => {
    const index = photos.findIndex((item) => item.id === photoId);
    updateStage(index >= 0 ? index : 0);
    modalInstance.show();
  };

  const triggers = gallery.querySelectorAll('[data-gallery-trigger]');
  triggers.forEach((trigger) => {
    trigger.addEventListener('click', (event) => {
      if (!photos.length) {
        return;
      }
      const id = Number.parseInt(trigger.getAttribute('data-gallery-photo-id') ?? '', 10);
      if (Number.isNaN(id)) {
        return;
      }
      event.preventDefault();
      openModalAt(id);
    });
  });

  const openers = gallery.querySelectorAll('[data-gallery-open]');
  openers.forEach((opener) => {
    opener.addEventListener('click', (event) => {
      if (!photos.length) {
        return;
      }
      event.preventDefault();
      openModalAt(photos[0].id);
    });
  });

  modalEl.addEventListener('shown.bs.modal', () => {
    document.addEventListener('keydown', handleKey);
    const initialFocus = (!prevBtn || prevBtn.disabled) ? nextBtn : prevBtn;
    if (initialFocus) {
      initialFocus.focus();
    }
  });

  modalEl.addEventListener('hidden.bs.modal', () => {
    document.removeEventListener('keydown', handleKey);
  });
}

function boot() {
  const galleries = document.querySelectorAll('[data-gallery]');
  galleries.forEach((gallery) => initGallery(gallery));
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot, { once: true });
} else {
  boot();
}
