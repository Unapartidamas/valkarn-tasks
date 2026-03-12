import en        from './en';
import es        from './es';
import zhHans    from './zh-Hans';
import fr        from './fr';
import de        from './de';
import ptBR      from './pt-BR';
import ja        from './ja';
import ru        from './ru';
import ar        from './ar';
import hi        from './hi';

const locales = {
  en,
  es,
  'zh-Hans': zhHans,
  fr,
  de,
  'pt-BR':   ptBR,
  ja,
  ru,
  ar,
  hi,
};

export default function getPageData(locale) {
  return locales[locale] || locales.en;
}
