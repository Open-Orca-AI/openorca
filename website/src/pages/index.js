import clsx from 'clsx';
import Link from '@docusaurus/Link';
import useDocusaurusContext from '@docusaurus/useDocusaurusContext';
import Layout from '@theme/Layout';
import HomepageFeatures from '@site/src/components/HomepageFeatures';

import styles from './index.module.css';

function HomepageHeader() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <header className={styles.heroBanner}>
      {/* Bubbles */}
      <div className={styles.bubbles} aria-hidden="true">
        <div className={clsx(styles.bubble, styles.bubble1)} />
        <div className={clsx(styles.bubble, styles.bubble2)} />
        <div className={clsx(styles.bubble, styles.bubble3)} />
        <div className={clsx(styles.bubble, styles.bubble4)} />
        <div className={clsx(styles.bubble, styles.bubble5)} />
        <div className={clsx(styles.bubble, styles.bubble6)} />
        <div className={clsx(styles.bubble, styles.bubble7)} />
        <div className={clsx(styles.bubble, styles.bubble8)} />
        <div className={clsx(styles.bubble, styles.bubble9)} />
        <div className={clsx(styles.bubble, styles.bubble10)} />
        <div className={clsx(styles.bubble, styles.bubble11)} />
        <div className={clsx(styles.bubble, styles.bubble12)} />
      </div>

      {/* Waves */}
      <div className={styles.waves} aria-hidden="true">
        <svg className={clsx(styles.wave, styles.wave1)} viewBox="0 0 1440 160" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M0,64 C360,120 720,10 1080,80 C1260,110 1380,60 1440,72 L1440,160 L0,160 Z" fill="#22d3ee" />
        </svg>
        <svg className={clsx(styles.wave, styles.wave2)} viewBox="0 0 1440 160" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M0,96 C240,40 480,130 720,80 C960,30 1200,110 1440,64 L1440,160 L0,160 Z" fill="#0e7490" />
        </svg>
        <svg className={clsx(styles.wave, styles.wave3)} viewBox="0 0 1440 160" preserveAspectRatio="none" xmlns="http://www.w3.org/2000/svg">
          <path d="M0,112 C180,70 360,140 540,90 C720,40 900,120 1080,70 C1260,20 1380,100 1440,80 L1440,160 L0,160 Z" fill="#091b2a" />
        </svg>
      </div>

      {/* Content */}
      <div className={clsx('container', styles.heroInner)}>
        <img
          src={require('@site/static/img/orca_mascot.png').default}
          alt="OpenOrca Mascot"
          className={styles.heroLogo}
        />
        <h1 className={styles.heroTitle}>{siteConfig.title}</h1>
        <p className={styles.heroSubtitle}>{siteConfig.tagline}</p>
        <div className={styles.buttons}>
          <Link className={styles.btnPrimary} to="/docs/">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M2 3h6a4 4 0 0 1 4 4v14a3 3 0 0 0-3-3H2z" />
              <path d="M22 3h-6a4 4 0 0 0-4 4v14a3 3 0 0 1 3-3h7z" />
            </svg>
            Get Started
          </Link>
          <Link
            className={styles.btnOutline}
            href="https://github.com/open-orca-ai/openorca/releases/latest">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" />
              <polyline points="7 10 12 15 17 10" />
              <line x1="12" y1="15" x2="12" y2="3" />
            </svg>
            Download
          </Link>
        </div>
      </div>
    </header>
  );
}

export default function Home() {
  const {siteConfig} = useDocusaurusContext();
  return (
    <Layout
      title={siteConfig.title}
      description={siteConfig.tagline}>
      <HomepageHeader />
      <main>
        <HomepageFeatures />
      </main>
    </Layout>
  );
}
