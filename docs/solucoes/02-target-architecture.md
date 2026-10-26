# Soluções — arquitetura alvo e justificativas

## Decisão
Monólito modular .NET 10, com API e Worker em processos separados para permitir que falha no processamento de consolidação não indisponibilize a escrita de lançamentos. Dois módulos independentes em código, persistência e ownership. Ver [diagramas C4](../diagrams/architecture.md) e [ADR](../adr/ADR-001.md).

## Fluxo
1. API recebe POST e grava Transactions + Outbox na mesma transação PostgreSQL.
2. Publisher lê Outbox e envia evento persistente para RabbitMQ, com confirmação de publicação.
3. Worker consome, verifica Inbox e atualiza consolidado no schema Consolidation em uma transação.
4. API consulta projeção do saldo por dia; dados eventualmente consistentes.

## Justificativa das tecnologias
| Escolha | Benefício | Trade-off |
|---|---|---|
| Modular monolith | Menor custo operacional e boa separação lógica | Falhas de infraestrutura compartilhada |
| .NET 10 | Stack do desafio, performance e produtividade | Dependência do ecossistema .NET |
| PostgreSQL / schemas | Simplicidade local e transações ACID | Banco único é ponto comum de falha |
| RabbitMQ | Desacoplamento temporal e buffering | Operação adicional e duplicatas possíveis |
| Outbox + Inbox | Reduz risco de perda e duplicidade de efeitos | Consistência eventual e complexidade moderada |
| Polly | Resiliência controlada a falhas transitórias | Configuração incorreta pode amplificar incidentes |

## Segurança de integração
Credenciais por módulo, grants mínimos, autenticação futura via OAuth2/JWT, TLS em produção, validação de payloads, limites de requisição e segredo em vault. Em Docker local, variáveis de desenvolvimento não são configuração de produção.

## Disponibilidade e escala
Separar escalabilidade do endpoint de leitura e throughput do Worker. A arquitetura suporta evolução para réplicas de API e consumidores; banco e broker únicos no Compose **não garantem HA**. Circuit breaker no acesso ao broker deve proteger recursos, sem impedir a persistência de Transactions.
