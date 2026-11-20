import React from 'react';
import { FontAwesomeIcon } from '@fortawesome/react-fontawesome';
import {
  faBan, faBookOpen, faBuilding, faCashRegister, faChair, faChartLine, faChevronDown, faCircleQuestion, faGear, faHeadset,
  faKey, faPen, faReceipt, faRightFromBracket, faRotateLeft, faTrashCan, faTriangleExclamation, faUserXmark, faUtensils, faXmark,
} from '@fortawesome/free-solid-svg-icons';

// Font Awesome Free icons (icons CC BY 4.0, code MIT), listed on the About page. Only the icons named here are bundled.
const ICONS = {
  ban: faBan,
  'book-open': faBookOpen,
  building: faBuilding,
  'cash-register': faCashRegister,
  chair: faChair,
  'chart-line': faChartLine,
  'chevron-down': faChevronDown,
  'circle-question': faCircleQuestion,
  gear: faGear,
  headset: faHeadset,
  key: faKey,
  pen: faPen,
  receipt: faReceipt,
  'right-from-bracket': faRightFromBracket,
  'rotate-left': faRotateLeft,
  'trash-can': faTrashCan,
  'triangle-exclamation': faTriangleExclamation,
  'user-xmark': faUserXmark,
  utensils: faUtensils,
  xmark: faXmark,
};

// Decorative icon: the button or link around it keeps its own words or aria-label for screen readers.
export default function Icon({ name, className }) {
  const icon = ICONS[name];
  if (!icon) return null;
  return <FontAwesomeIcon icon={icon} className={className} fixedWidth aria-hidden="true" />;
}
