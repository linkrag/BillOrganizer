# Decisões

- **Fonte fora de `Mods`** (7/nov/2026, escolha do usuário entre as opções): o fonte e os docs ficam em `RimWorld/_dev/src/<mod>`; `Mods/<mod>` é cópia gerada pelo deploy (só About, LoadFolders.xml, 1.6, Bin), e o upload continua pelo botão do jogo. Alternativa recusada: manter tudo e publicar pelo SteamCMD a partir de uma cópia limpa (exige login no SteamCMD e proíbe o botão do jogo).
- **Janela própria com layout parecido com o Nice Bill Tab, sem copiar código ou arte** (2/nov): o usuário gosta do visual dele e do Dubs; as DLLs são fechadas e de outros autores, então só a ideia (lista + painel de detalhe + barras) foi reaproveitada. Paleta, texturas e código são nossos.
- **Compat com o Nice Bill Tab por um botão na bancada, não por hook nas funções dele** (3/nov): o Nice não tem API pública nem licença de reuso e troca o `FillTab` inteiro. Só lemos a flag `EnabledMod` (reflexão). Os outros mods "Nice" não tocam em bills.
- **Glifos como textura** (3/nov): estrela, setas e check não são texto, a fonte do jogo não garante Unicode.
- **CE por reflexão** (2/nov): sem referência à DLL do CE, mesmo princípio do Nice Bill Tab; campos ausentes viram "-".
- **Grupos de "Outros" dinâmicos** (3/nov): vêm das `ThingCategoryDef` do produto, para qualquer mod cair num grupo sem lista a manter.
- **Busca por texto pré-montado e prefixos próprios** (3/nov): o texto de busca de cada receita é montado uma vez (`RecipeEntry.MatchesSearch`); `@ % #` espelham a ideia do Nice, mas a implementação é nossa. Trabalho/Habilidade/Valor são colunas comuns a todos os grupos (aparecem depois das de combate e são cortadas primeiro quando falta largura).
- **Fila dentro da nossa janela, não na aba vanilla** (3/nov): o Nice substitui a aba e já tem fila; o vanilla já copia/cola/reordena. O `QueuePanel` só usa o `BillStack`, então serve a qualquer aba e também à janela "Tarefas" da barra. Sem patch em `Bill.DoInterface`.
- **Templates em arquivo próprio em `Config/`, lidos sob demanda** (3/nov): o `ModSettings` carrega no construtor do mod, antes dos defs (o Dubs precisa limpar o XML cru por isso). Lendo depois com `Scribe.loader.InitLoading` as `Bill` resolvem receita e filtros normalmente. Ao salvar clonamos e zeramos referências de save (pawnRestriction, precept, xenogerm, boundUft, estoque específico). Diferenças em relação ao Dubs: respeita o limite de 15, avisa o que pulou, lista todos os tipos de bill, loga erros.
- **Opções da tarefa sem tocar no `Dialog_BillConfig`** (3/nov): modo/quantidade/material são aplicados na `Bill_Production` recém criada; o material só desliga os outros stuffs do filtro compartilhado.
- **Aba compacta por postfix, sem substituir o FillTab** (3/nov): o `FillTab` vanilla roda inteiro (cola, fila, botões do bill, config) e só encolhemos o rect da fila (`ref Rect` no prefix de `DoListing`, só dentro da aba) e desenhamos depois (4/nov: lista de receitas à esquerda + `RecipeDetail` à direita; a grade de ícones foi removida a pedido do usuário, que queria nomes e detalhes sem abrir a janela; o painel de detalhe virou a classe `RecipeDetail`, compartilhada com a janela). Assim não competimos com o Nice: se `NiceCompat.TabReplaced`, nada é alterado. Tamanho da aba via `size` (campo protegido, Traverse).
- **Armadura por material**: coluna e barras usam o melhor material permitido; o detalhe mostra a faixa e marca o melhor material em estoque.

## Link de bancadas (3/nov)
- Escolhido o modelo **grupo com fila espelhada** (como o linked storage), não link por tarefa (modelo do Better Workbench Management, Workshop 935982361, só até 1.6 e não ativo aqui). O vanilla não tem nada equivalente para bills (só `StorageGroup` para storage; verificado por busca de strings na DLL).
- **Polling por assinatura** em vez de patch em setters/`AddBill`/`Delete`/`Reorder`: funciona com qualquer aba e mod que edite bills, sem depender de assinaturas que mudem. Custo: atraso de até 0,3 s e "primeira alteração ganha" em edições simultâneas.
- Só bancadas do mesmo def e mesmo mapa (garante as mesmas receitas). A origem do clique vence (a outra perde as tarefas, com confirmação).
- Sem reserva, duas bancadas linkadas iniciariam o último item juntas. `ShouldDoNow` (postfix) devolve false para a tarefa que ninguém está executando quando os peers já cobrem o que falta; o contador continua um só.
- O contador "fazer X vezes" é o único estado que muda sozinho; tratado com postfix em `Bill_Production.Notify_IterationCompleted`. O campo `paused` (automático) fica fora da assinatura.

## "Dá para fazer N", painel de fixados e nome (3/nov, noite)
- **Fonte = ResourceCounter** (escolha do usuário): bate com a lista de recursos à esquerda, é barato (1x/s, já usado) e evita varrer o mapa. Custo: ignora itens soltos, proibidos e alcance/raio da tarefa.
- **Fórmula**: lotes = min(floor(have/need)) nas unidades da receita (`IngredientValueGetter`), com pool compartilhado quando linhas aceitam o mesmo def; itens = lotes x contagem do primeiro produto. Descontar o que a fila já reserva ficou de fora (anotado como possível opção).
- **Patches do readout manuais** (`ResourcePanel.TryPatch`), sem atributos, para uma falha não derrubar o `PatchAll`; só dois métodos (`Listing_ResourceReadout.DoThingDef` prefix/postfix e `ResourceReadout.ResourceReadoutOnGUI` postfix). O painel fica ao lado da lista (x=130) em vez de dentro dela, porque a lista vanilla rola/cresce e o painel tem de ficar sempre visível. `curY` é protegido: `AccessTools.FieldRefAccess`.
- **Nome**: o mod passa a ser mais que bills; nome de exibição "Colony Organizer". **packageId `linkrag.billorganizer`, pasta, namespace e DLL não mudam**: o save guarda o `BillLinkManager`, há `Config/BillOrganizer_Templates.xml` e as opções atrelados ao id. Renomeio completo (com migração) só se pedido.
- Ideias de outras áreas organizáveis (filtros de storage, pesquisa, plantio, equipamento, resumo de produção travada): `Docs/IDEAS.md`.
- **Lista de recursos própria em vez de enfeitar a do jogo** (3/nov, noite): o readout vanilla (`Listing_ResourceReadout`) só tem ícone + número e uma árvore fixa; nome, ordem e busca exigiriam reescrever o desenho de qualquer jeito. O prefix pula o `ResourceReadoutOnGUI` inteiro e desenha do zero com os mesmos dados; falha = volta ao vanilla. Os fixados viraram seção no topo e o painel lateral só vale com a lista nova desligada.


## Fila em cartões próprios (3/nov)
A fila vanilla da aba compacta (botões amarelos) foi trocada por cartões nossos (QueuePanel + BillCard), no estilo moderno que o usuário pediu a partir do Nice Bill Tab. Só a ideia/aparência geral; nenhum código ou arte do Nice foi lido ou copiado. Edição rápida no cartão (modo, quantidade, material, pausa) e filtros avançados no Dialog_BillConfig vanilla (botão '...'), para não reescrever os muitos casos dele. Material mostrado como ícone+nome (1), ícones (2-3) ou 'N de M'; escolha por legibilidade em fila longa. Falha do desenho = volta à fila vanilla (regra de ouro 1).

## Reordenar com ReorderableWidget e ícone prancheta (3/nov)
Reordenar usa o widget do jogo, não eventos próprios. O ícone único (Catalog.png, barra e bancada) virou uma prancheta com checklist: lê como 'tarefas' sem texto, e a seta de ordenar antiga sugeria outra coisa.

## Prioridade de bancada via GetPriority (4/nov)
- O jogo escolhe o tipo de trabalho pela aba Trabalho e, dentro de cada `WorkGiver`, o alvo de maior `GetPriority` e depois o mais próximo (`JobGiver_Work`, verificado no IL 1.7; `WorkGiver_DoBill` não sobrescreve, devolve 0). Somar a prioridade da bancada ali é o gancho mínimo e funciona com qualquer aba. Não cruza tipos de trabalho: isso continua sendo da aba Trabalho (dito ao usuário, que queria Fabricação antes de Stonecutting).
- "Até X" usa o mesmo `CountProducts` do vanilla (não recontamos estoque por conta própria).

## Prioridade de bancada também entre WorkGivers da mesma habilidade (5/nov)
- Troca parcial da decisão de 4/nov: o `GetPriority` continua (desempate no mesmo WorkGiver), mas o usuário quer Fabrication antes de Smithy (mesma habilidade, WorkGivers diferentes). O `JobGiver_Work` usa o primeiro WorkGiver da lista `WorkGiversInOrderNormal` que acha alvo (IL 1.7), então reordenamos só os `WorkGiver_DoBill` dentro de cada `workType` pelos valores das bancadas, em vez de mexer no laço ou no `priorityInType` dos defs (que valeria para todas as bancadas).
- Escolhido o postfix nos getters (a lista é reconstruída só quando suja) com cache; risco: mods que reordenam essa lista (abas de trabalho). Mitigação: prioridade Last e opção para desligar. Nunca cruza habilidades.

## UX/UI de 5/nov: texto adaptável em vez de largura fixa
- **Chip de modo** sem teto fixo: as formas candidatas vão da mais completa para a mais curta e vale a primeira que cabe; o corte ("...") é o último recurso (Maersk/Primer/SAP: truncar só sem espaço, números por último). Until X mostra `atual/alvo`, a mesma forma do `RepeatInfoText` vanilla (decompilado, 1.7) e do "tem/precisa" dos Requisitos; a palavra "Until" fica no seletor de modo e na tooltip (o usuário escolheu "só 130/1000"). Não renomeamos nenhum modo.
- **Estado correto antes de bonito**: "Concluída" quando "fazer X vezes" chega a 0 (o vanilla mantém a tarefa na pilha); "not now" só quando é isso mesmo. Cor sempre acompanhada de palavra ou tooltip (Carbon).
- **Nome de receita**: sem verbo -> corte no meio com o sufixo de lote. O verbo vem da chave `RecipeMake` do jogo, então vale em qualquer idioma.
- **Sem "x" nos lotes** da lista: "x" já é repetição ("200x") e tamanho de lote ("x200"). A coluna se chama "Lotes".
- **Cor**: números em branco; a cor do atributo fica na barra e no cabeçalho; vermelho só para "falta/ruim".
- **Ícones**: um ícone, um significado (Duplicar e Vincular não reusam o Colar). Os botões vanilla (pausar etc.) ficam como o jogo desenha.
- **PT-BR**: seguimos a palavra do jogo (vincular, modelo, desafixar) em vez de anglicismos.

## Ajustes de 5/nov (11:25) pós-teste
- **Orçamento de colunas**: o nome da receita mantém 170 px (era 190) e a árvore se adapta ao maior rótulo (190-230 px): perder a coluna Value por 20 px de árvore/coluna era pior do que um nome 20 px mais curto (que já se adapta com `FitName`).
- **Filtro de munição**: com muitos tipos (CE tem 20+), um botão com menu é mais claro do que um chip alfabético qualquer + "+N"; chips só quando todos cabem.
- **Corte de nome** sempre em palavra inteira; um corte no meio da palavra escondia a parte que distingue.

## Nome acima de coluna (5/nov, 11:36)
O piso do nome (190) volta a mandar no orçamento de colunas: quando falta espaço cai a última coluna (Value) em vez de encolher o nome. Testado no caso mais apertado (munição CE: Guns + 4 colunas): com o nome a ~156 unidades quatro receitas ficavam iguais. A mesma regra vale para o corte de nome: nunca deixar só um toco da primeira palavra.

## Corte de nome que preserva o tipo (5/nov, 12:01)
Receitas de munição do CE têm a forma `<calibre> (<tipo>) <palavra genérica> x<lote>`; o que distingue as linhas é o `(tipo)`. Ordem do corte: inteiro, sem verbo, sem a palavra genérica depois do tipo, começo cortado em palavra mantendo `(tipo) x<lote>`, fim cortado. Tooltip com o nome inteiro só quando algo foi cortado.

## FitName: o que cai e o que fica (5/nov)
Ordem de sacrifício: verbo ("Make "), palavra genérica depois do tipo (lista fechada) ou cópia dela antes do tipo, parte inicial do prefixo (corte em palavra, ou mais longo se só sobraria um toco), e só por último o fim do nome. O tipo "(AP-HE)" e o lote "x200" são o que distingue as linhas de munição e nunca caem antes do prefixo. Palavras fora da lista genérica não somem às cegas (podem distinguir receitas). Cache por (nome, largura) por causa do custo por quadro.

## "Até X" em lotes (5/nov)
O passo e o atalho de lote seguem o vanilla (`recipe.targetCountAdjustment`, ou o tamanho do produto). Pedido do usuário: a meta já começa em 1 lote; só trocamos um valor que nós mesmos pusemos ou que está abaixo de 1 lote.

## Regras de grupo por dados, não por lista (5/nov)
Roupa: Torso > cabeça (Neck não conta) > pernas > mãos > pés > acessório; camada de acessório só vale sem camada de corpo. Granada pela categoria Grenades antes da tag; lançador em WeaponsRanged é Ranged; arma só se estiver em Weapons/Grenades (ou sem categoria) e não for comestível/material. Receita sem produto vai para Processamento/Mecanoides/Outros pelos ingredientes, nunca por defName. Conferência em jogo pelo botão "Dev" (dump no log).

## Processamento, Rituais e Arremesso (5/nov, 15:18)
Receitas que destroem, fundem, desmontam ou reciclam vão para Processamento mesmo quando têm produto. A regra olha só as entradas (munição, arma, roupa, ou o produto igual ao custo do item); cadáver não entra, para a extração de combustível ficar junto das irmãs. Receita de subclasse de `RecipeDef` sem produto é ritual. Arma de arremesso e categorias próprias de mods sob Weapons viram nós irmãos de Granadas. A subdivisão de Distância ficou para depois: precisa de família de munição/pente do CE, que o dump ainda não tinha.

## Nome com o mod quando dois itens têm o mesmo nome (5/nov)
Só quando o nome (sem diferenciar maiúsculas) se repete na bancada e os mods são diferentes; o resto fica igual.

## 3º nível só onde a lista é longa (5/nov, 15:25)
Subgrupos (Distância, Cabeça, Torso) só aparecem quando o nó tem mais de 15 receitas na bancada e pelo menos 2 tipos. Famílias de produtos com nível (robôs, gemas) só em nós com mais de 12 receitas, com 2+ famílias, e nunca uma família cobrindo o nó inteiro. Um nível com um filho só nunca é criado. Distância: tags primeiro (do mod), dados do CE depois, Fuzis como resto.

## Material escolhido manda no detalhe (5/nov)
Com um material escolhido, stats e valor mostram esse material e o "dá para fazer" conta só ele; "Qualquer" mostra a faixa. A lista de materiais também escolhe (clique).

## Presets definem e somam; -/+ anda 1 unidade; teclas do jogo (11/nov, substitui a de 10/nov)
- Pedido do usuário: o 1º clique num preset define o valor e marca o botão; clicar de novo no marcado soma o valor dele. O -/+ anda sempre 1 unidade do modo (1 repetição; 1 lote em "Até X"). Os presets continuam múltiplos 1x/5x/25x/50x da unidade.
- Ctrl/Shift seguem as teclas do jogo (`GenUI.CurrentAdjustmentMultiplier`: Ctrl x10 e Shift x100 por padrão, configuráveis em Opções > Teclado), como no diálogo vanilla da tarefa. As tooltips mostram as teclas configuradas. Se o usuário preferir Shift x10 fixo, a troca fica num ponto só (`Ui.StepMult`).
- O cartão da fila em "Até X" anda por lote e encaixa no múltiplo do lote.

## Presets de quantidade = múltiplos da unidade do modo; o preset é o passo (10/nov) - SUBSTITUÍDA em 11/nov
O usuário escolheu entre as opções propostas:
- Os presets são 1x/5x/25x/50x da unidade do modo: 1 repetição em "Fazer X vezes" e 1 lote (`TargetStep`) em "Até X".
- "Fazer X vezes" continua contando repetições, sem converter para itens.
- O preset clicado define o valor e o passo do -/+, e o passo fica guardado (multiplicador, campo do `RecipeDetail`, não salvo) até clicar em outro preset.
- O chip destacado é o passo ativo, não o valor.

## "Copiar fila" em memória; um só "Colar" (10/nov)
- A área de transferência do jogo guarda uma tarefa só, por isso a fila copiada fica num modelo sem nome, em memória (`Templates.CopyQueue`).
- Fica um botão "Colar" só, que cola o que foi copiado por último: a fila ou a tarefa do cartão ou da aba vanilla.
- A fila copiada vale só no jogo em que foi copiada e mantém as referências, como a cópia vanilla. Para levar a outro save, use os modelos, que limpam as referências.

## Nomes de subgrupo cabem na árvore (5/nov)
A árvore continua com teto de 230 px (as colunas da lista valem mais). Por isso, nome de nó do 3º nível tem no máximo uns 15 caracteres, em EN e PT-BR; o nome inteiro fica na tooltip.

## Bônus lidos das Defs e dos textos do próprio jogo; sem cor de bom/ruim (11/nov)
- Os bônus vêm de campos e comps genéricos (`equippedStatOffsets`, `CompProperties.SpecialDisplayStats`, hediffs, ingeríveis) e dos `StatDrawEntry` que o cartão de info usa, em vez de uma lista de stats escrita à mão. Assim, mods que usam os mesmos campos aparecem sem código novo, e o texto sai na língua e no formato do jogo.
- `StatDef` não tem "maior é melhor". Por isso os valores de bônus ficam em branco (só o sinal), e o Comparar não pinta verde/vermelho nas linhas de bônus. Pintar erraria stats como limiar de colapso mental.
- Nutrição, alegria e textos genéricos de comps não acendem a marca da lista (`minor`): a marca quer dizer "faz algo além do esperado", não "tem qualquer informação".
- Colunas de bônus: no máximo 2, e só para stats que várias receitas do grupo têm, para não tirar o espaço do nome (regra dos 190 px).

## Aba compacta assume o colar e o X (12/nov)
- O colar e o X vanilla da aba de tarefas são escondidos só na aba compacta e substituídos pelos nossos, no estilo do mod. O colar fica ao lado de "Ações" e o X no cabeçalho do painel de detalhe, com o nome da bancada.
- Esconder o colar = esvaziar `BillUtility.Clipboard` durante `ITab_Bills.FillTab` (o vanilla só desenha com clipboard). Não usamos transpiler: o jeito escolhido não depende do IL. Todo código nosso lê o clipboard por `Patch_BillTab.Clipboard`.
- Esconder o X = prefix em `Widgets.CloseButtonFor` com guarda estreita (ImmediateWindow + aba aberta `ITab_Bills` + aba compacta ativa). Fora disso o método roda intacto.

## Receita indisponível nunca é mostrada (12/nov)
O pedido do usuário é não mostrar item bloqueado ou que não existe mais. Por isso o clique numa tarefa cuja receita a bancada não oferece agora só dá uma mensagem, na aba compacta e na janela; a entrada "só consulta" foi removida. As listas e o detalhe só mostram o que pode ser feito.

## Abate automático: bebês protegidos por padrão e fora dos limites (13/nov)
- Bebê rende ~20% da carne (`StatPart_BodySize` x bodySizeFactor 0.2), então o padrão é **não** abater bebês (escolha do usuário); libera por animal.
- Bebê protegido **não conta** nos limites (escolha do usuário), igual ao vanilla com vinculado não permitido. Assim "máx. 2 machos jovens" = 2 juvenis.
- Bebê = 1º de vários estágios de vida e não reprodutivo: animais não têm `developmentalStage`, e isso cobre raças de mod.
- Estado num `MapComponent` porque os configs vanilla são por mapa; não estendemos `AutoSlaughterConfig` (classe do jogo, salva por ele).
- Filtro por transpiler trocando só a chamada de `CanAutoSlaughterNow` dentro do getter (não um prefix no método, que outros lugares podem usar). Sem achar exatamente 1 chamada, fica vanilla.
- Coluna desenhada por postfix no fim da linha, com a janela até 140 px mais larga e o nome 140 px mais estreito. Ela sempre aparece com a feature ligada: proteção sem o checkbox prenderia os bebês. Pelo mesmo motivo, uma falha na janela desliga também a proteção.

## Força-tarefa: prioridade de trabalho temporária (13/nov/2026)
- Pedido do usuário: depois de uma invasão, ~100-150 animais saíram do curral pela cerca quebrada e só 3-4 colonos tinham Lidar com animais em 1. Ele queria um mutirão por tempo limitado que depois volte sozinho.
- **Botão na aba Trabalho vanilla** (escolha do usuário entre aba Trabalho, gizmo nos colonos e os dois). Postfix em `MainTabWindow_Work.DoWindowContents`, no espaço livre entre o checkbox "Prioridades manuais" (x 5-145) e o texto "<= Maior prioridade" (x 370), posições lidas no IL 1.7. O status das ativas vai à direita do "Menor prioridade =>" (x 790) quando cabe; se não cabe, vai na tooltip do botão.
- **Estado num `GameComponent`** (como `BenchPriority`): a força-tarefa vale para a colônia inteira e precisa sobreviver a salvar e carregar. Os pawns são guardados por `Scribe_References`, e uma referência que some ao carregar tira o membro da lista.
- **Restaurar só se o colono ainda tem o valor da força-tarefa** (escolha do usuário): a mudança manual feita no meio sempre vence. Com prioridades manuais desligadas, o `GetPriority` lê só 0/3, então a comparação é ligado/desligado. Nesse modo a força-tarefa só liga o trabalho de quem estava com 0, e o diálogo oferece ligar as prioridades manuais.
- **Força-tarefa sobre força-tarefa**: o par (colono, trabalho) passa para a nova e mantém o valor de antes da 1ª, para nunca "restaurar" para o 1. A força-tarefa antiga que fica vazia some.
- **Interromper só trabalho comum** (work giver, não forçado pelo jogador, não convocado) e só quando a nova prioridade vence a do trabalho atual (número menor, ou igual com coluna mais à esquerda). Nunca interrompe trabalho de cuidado (Médico, Paciente, Repouso, Bombeiro), work giver de emergência, job não `casualInterruptible` nem quem carrega pawn ou corpo. A revisão por agentes mostrou que todo job de work giver tem `workGiverDef`, então sem essa lista um médico largaria o ferido no chão. Comida, sono e ordens manuais nem passam pelo work giver.
- **Sempre gravar com `SetPriority`** (início e fim), nunca pular por "já está igual": com prioridades manuais desligadas, o `GetPriority` mascara o valor cru.
- Diálogo: todos os capazes já vêm marcados (escolha do usuário). Mostra a prioridade atual e a habilidade relevante. As últimas escolhas (trabalhos, horas, prioridade, interromper) ficam nas opções do mod.
- Limitações aceitas: se o mod for removido com uma força-tarefa ativa, as prioridades ficam no valor dela. Dentro do mesmo número, o jogo continua seguindo a ordem das colunas (Bombeiro, Paciente, Médico, Básico, Carcereiro antes de Lidar com animais); o mod não rebaixa outros trabalhos.

## Passo de "Até X" = o que uma construção produz (13/nov/2026)
- Pedido do usuário: vale para todos os itens. O `RecipeDef.targetCountAdjustment` é reescrito em runtime para `products[0].count` (só receitas com produto), porque o core só o lê nos -/+ da tarefa (`Bill_Production.DoConfigInterface`): mudança só de UI e que alcança vanilla, Nice e outros mods sem patch em cada um. Aplicado em `Game.FinalizeInit` (todas as defs prontas, inclusive as geradas pelo CE). Opção para desligar, com os valores originais guardados para restaurar. A nossa UI usa a mesma regra (`RecipeEntry.TargetStepOf`) com a opção ligada ou não.
